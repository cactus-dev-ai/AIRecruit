from __future__ import annotations

import json
from datetime import datetime
from pathlib import Path

import pyodbc

from app.database import build_connection_string
from app.resume_agent.errors import ResumeImportError
from app.resume_agent.models import ImportTask, MatchScoreTask, ParsedResume, PreparedResume, ResumeAnalysisTask
from config.settings import get_settings


class TaskRepository:
    def expire_abandoned_match_score_tasks(self) -> int:
        """Requeue an interrupted match task once; fail it after its retry budget is exhausted."""
        sql = """
        SET NOCOUNT ON;
        UPDATE dbo.AITask SET
          Status=CASE WHEN RetryCount<MaxRetryCount THEN 'WAITING' ELSE 'FAILED' END,
          RetryCount=CASE WHEN RetryCount<MaxRetryCount THEN RetryCount+1 ELSE RetryCount END,
          NextRunTime=CASE WHEN RetryCount<MaxRetryCount THEN SYSUTCDATETIME() ELSE NextRunTime END,
          ErrorMessage=CASE WHEN RetryCount<MaxRetryCount
                            THEN N'岗位评分执行中断，已自动重新排队。'
                            ELSE N'岗位评分执行中断且已达到自动重试上限。' END,
          WorkerId=NULL,LockedUntil=NULL,
          FinishTime=CASE WHEN RetryCount<MaxRetryCount THEN NULL ELSE SYSUTCDATETIME() END,
          UpdateTime=SYSUTCDATETIME()
        WHERE TaskType='MATCH_SCORE' AND Status='RUNNING' AND LockedUntil<SYSUTCDATETIME();
        UPDATE r SET Status=t.Status,ErrorMessage=t.ErrorMessage,
          FinishTime=CASE WHEN t.Status='FAILED' THEN t.FinishTime ELSE NULL END,UpdateTime=SYSUTCDATETIME()
        FROM dbo.ScoringRunResult r JOIN dbo.AITask t ON t.ScoringRunId=r.ScoringRunId AND t.CandidateJobId=r.CandidateJobId
        WHERE t.TaskType='MATCH_SCORE' AND t.Status IN('WAITING','FAILED') AND r.Status='RUNNING';
        SELECT @@ROWCOUNT;
        """
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            row = connection.cursor().execute(sql).fetchone()
            return int(row[0]) if row else 0

    def expire_abandoned_resume_analysis_tasks(self) -> int:
        sql = """
        SET NOCOUNT ON;
        UPDATE t SET Status='CANCELLED',ErrorMessage=N'简历已产生新版本，旧版本解析任务自动取消。',
          FinishTime=SYSUTCDATETIME(),WorkerId=NULL,LockedUntil=NULL,UpdateTime=SYSUTCDATETIME()
        FROM dbo.AITask t JOIN dbo.Resume r ON r.Id=t.ResumeId
        WHERE t.TaskType='RESUME_PARSE' AND t.Status='WAITING' AND (r.IsDeleted=1 OR r.IsCurrent=0);

        UPDATE dbo.AITask SET
          Status='FAILED',
          ErrorMessage=N'AI简历分析任务租约过期，等待自动清理。',FinishTime=SYSUTCDATETIME(),
          WorkerId=NULL,LockedUntil=NULL,UpdateTime=SYSUTCDATETIME()
        WHERE TaskType='RESUME_PARSE' AND Status='RUNNING' AND LockedUntil<SYSUTCDATETIME();
        SELECT @@ROWCOUNT;
        """
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            row = connection.cursor().execute(sql).fetchone()
            return int(row[0]) if row else 0

    def find_failed_resume_analysis(self) -> ResumeAnalysisTask | None:
        sql = """
        SELECT TOP (1) t.Id,r.Id,r.CandidateId
        FROM dbo.AITask t JOIN dbo.Resume r ON r.Id=t.ResumeId
        WHERE t.TaskType='RESUME_PARSE' AND t.Status='FAILED'
        ORDER BY t.Id;
        """
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            row = connection.cursor().execute(sql).fetchone()
            if row is None:
                return None
            return ResumeAnalysisTask(task_id=int(row[0]), resume_id=int(row[1]), candidate_id=int(row[2]), raw_text="")

    def claim_resume_analysis(self) -> ResumeAnalysisTask | None:
        settings = get_settings()
        sql = """
        SET NOCOUNT ON;
        DECLARE @Claimed TABLE(TaskId BIGINT,ResumeId BIGINT);
        ;WITH NextTask AS (
          SELECT TOP (1) t.*
          FROM dbo.AITask t WITH (UPDLOCK,READPAST,ROWLOCK)
          JOIN dbo.Resume r ON r.Id=t.ResumeId
          WHERE t.TaskType='RESUME_PARSE' AND t.Status='WAITING'
            AND t.NextRunTime<=SYSUTCDATETIME() AND r.IsDeleted=0 AND r.IsCurrent=1
          ORDER BY t.Priority DESC,t.CreateTime ASC,t.Id ASC
        )
        UPDATE NextTask SET Status='RUNNING',WorkerId=?,LockedUntil=DATEADD(MINUTE,?,SYSUTCDATETIME()),
          StartTime=COALESCE(StartTime,SYSUTCDATETIME()),UpdateTime=SYSUTCDATETIME()
        OUTPUT inserted.Id,inserted.ResumeId INTO @Claimed;
        SELECT c.TaskId,r.Id,r.CandidateId,COALESCE(r.RawText,N'')
        FROM @Claimed c JOIN dbo.Resume r ON r.Id=c.ResumeId;
        """
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            row = connection.cursor().execute(sql, settings.worker_id, settings.worker_lease_minutes).fetchone()
            if row is None:
                return None
            return ResumeAnalysisTask(task_id=int(row[0]), resume_id=int(row[1]), candidate_id=int(row[2]), raw_text=str(row[3]))

    def claim_match_score(self) -> MatchScoreTask | None:
        settings=get_settings()
        sql="""
        SET NOCOUNT ON;
        DECLARE @C TABLE(TaskId BIGINT,ScoringRunId BIGINT,CandidateJobId BIGINT);
        ;WITH N AS(SELECT TOP(1)t.* FROM dbo.AITask t WITH(UPDLOCK,READPAST,ROWLOCK)
          JOIN dbo.CandidateJob cj ON cj.Id=t.CandidateJobId JOIN dbo.Candidate c ON c.Id=cj.CandidateId
          JOIN dbo.ScoringRun sr ON sr.Id=t.ScoringRunId
          JOIN dbo.ScoringRunResult rr ON rr.ScoringRunId=t.ScoringRunId AND rr.CandidateJobId=t.CandidateJobId
          WHERE t.TaskType='MATCH_SCORE' AND t.Status='WAITING' AND t.ScoringRunId IS NOT NULL AND t.NextRunTime<=SYSUTCDATETIME() AND c.IsDeleted=0
          ORDER BY t.Priority DESC,sr.CreateTime ASC,sr.Id ASC,
            rr.ResumeImportTime DESC,rr.ResumeId DESC,t.CandidateJobId DESC,t.Id DESC)
        UPDATE N SET Status='RUNNING',WorkerId=?,LockedUntil=DATEADD(MINUTE,?,SYSUTCDATETIME()),StartTime=COALESCE(StartTime,SYSUTCDATETIME()),UpdateTime=SYSUTCDATETIME()
        OUTPUT inserted.Id,inserted.ScoringRunId,inserted.CandidateJobId INTO @C;
        UPDATE r SET Status='RUNNING',ErrorMessage=NULL,FinishTime=NULL,StartTime=COALESCE(StartTime,SYSUTCDATETIME()),UpdateTime=SYSUTCDATETIME()
        FROM dbo.ScoringRunResult r JOIN @C x ON x.ScoringRunId=r.ScoringRunId AND x.CandidateJobId=r.CandidateJobId;
        SELECT x.TaskId,x.ScoringRunId,cj.Id,cj.CandidateId,cj.JobId FROM @C x JOIN dbo.CandidateJob cj ON cj.Id=x.CandidateJobId;
        """
        with pyodbc.connect(build_connection_string(),timeout=10) as connection:
            row=connection.cursor().execute(sql,settings.worker_id,settings.worker_lease_minutes).fetchone()
            return None if row is None else MatchScoreTask(task_id=int(row[0]),scoring_run_id=int(row[1]),candidate_job_id=int(row[2]),candidate_id=int(row[3]),job_id=int(row[4]))

    def load_match_context(self,task: MatchScoreTask) -> dict:
        with pyodbc.connect(build_connection_string(),timeout=30) as connection:
            cur=connection.cursor()
            row=cur.execute("""SELECT sr.JobName,sr.JobDescription,sr.JobRequirement,sr.SkillRequirement,sr.EducationRequirement,sr.MinExperienceMonths,
              sr.SkillWeight,sr.ProjectWeight,sr.ExperienceWeight,sr.EducationWeight,sr.DutyWeight,sr.ProductWeight,sr.IndustryWeight,sr.TenureWeight,sr.CompanyEnvironmentWeight,sr.ScoreConfigVersion,
              c.Name,cp.HighestEducation,cp.TotalExperienceMonths,cp.CurrentCompany,cp.CurrentTitle,cp.Summary,sr.JobProfileContent,sr.JobProfileVersion
              FROM dbo.CandidateJob cj JOIN dbo.ScoringRun sr ON sr.Id=? AND sr.JobId=cj.JobId JOIN dbo.Candidate c ON c.Id=cj.CandidateId
              LEFT JOIN dbo.CandidateProfile cp ON cp.CandidateId=c.Id
              WHERE cj.Id=?""",task.scoring_run_id,task.candidate_job_id).fetchone()
            if row is None: raise RuntimeError("candidate job not found")
            skills=[{"name":r[0],"level":r[1],"months":r[2],"evidence":r[3]} for r in cur.execute("SELECT SkillName,SkillLevel,ExperienceMonths,Evidence FROM dbo.CandidateSkill WHERE CandidateId=?",task.candidate_id).fetchall()]
            projects=[{"name":r[0],"role":r[1],"technology":r[2],"description":r[3]} for r in cur.execute("SELECT ProjectName,RoleName,Technology,Description FROM dbo.CandidateProject WHERE CandidateId=?",task.candidate_id).fetchall()]
            works=[{"company":r[0],"title":r[1],"start":str(r[2] or ''),"end":str(r[3] or ''),"description":r[4]} for r in cur.execute("SELECT CompanyName,JobTitle,StartDate,EndDate,Description FROM dbo.CandidateWorkExperience WHERE CandidateId=? ORDER BY StartDate DESC",task.candidate_id).fetchall()]
            educations=[{"school":r[0],"major":r[1],"degree":r[2],"description":r[3]} for r in cur.execute("SELECT SchoolName,Major,Degree,Description FROM dbo.CandidateEducation WHERE CandidateId=?",task.candidate_id).fetchall()]
            company_docs=[]
            for work in works:
                doc=cur.execute("SELECT TOP(1)Title,Content FROM dbo.KnowledgeDocument WHERE DocumentType='COMPANY_INFO' AND Status='ACTIVE' AND IsDeleted=0 AND (Content LIKE N'公司名称：'+?+N'%' OR Content LIKE N'%公司别名：%'+?+N'%') ORDER BY Version DESC",work["company"],work["company"]).fetchone()
                if doc: company_docs.append({"company":work["company"],"knowledge":str(doc[1])[:4000]})
            if row[22] is None: raise RuntimeError("scoring run job profile snapshot not found")
            return {"job":{"name":row[0],"description":row[1],"requirement":row[2],"skills":row[3],"education":row[4],"min_months":row[5],"profile":row[22],"profile_version":int(row[23])},
              "weights":{"skill":float(row[6]),"project":float(row[7]),"experience":float(row[8]),"education":float(row[9]),"experience_sub":{"duty":float(row[10]),"product":float(row[11]),"industry":float(row[12]),"tenure":float(row[13]),"company_environment":float(row[14])}},"score_version":int(row[15]),
              "candidate":{"name":row[16],"education":row[17],"experience_months":row[18],"current_company":row[19],"current_title":row[20],"summary":row[21],"skills":skills,"projects":projects,"works":works,"educations":educations},"company_knowledge":company_docs}

    def complete_match_score(self,task:MatchScoreTask,context:dict,result:dict,model:str,prompt_version:str)->None:
        w=context["weights"]; total=round(result["skill_score"]*w["skill"]/100+result["project_score"]*w["project"]/100+result["experience_score"]*w["experience"]/100+result["education_score"]*w["education"]/100,2)
        report=json.dumps({"scores":result,"weights":w,"jobProfileVersion":context["job"]["profile_version"],"companyKnowledgeCount":len(context["company_knowledge"])},ensure_ascii=False)
        sql="""UPDATE dbo.ScoringRunResult SET Status='SUCCESS',SkillScore=?,ProjectScore=?,ExperienceScore=?,EducationScore=?,AIScore=?,AIReport=?,Strengths=?,Weaknesses=?,Recommendation=?,ModelName=?,PromptVersion=?,ErrorMessage=NULL,FinishTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME()
        WHERE ScoringRunId=? AND CandidateJobId=? AND Status='RUNNING';
        UPDATE dbo.CandidateJob SET SkillScore=?,ProjectScore=?,ExperienceScore=?,EducationScore=?,AIScore=?,AIReport=?,Strengths=?,Weaknesses=?,Recommendation=?,ScoreConfigVersion=?,ModelName=?,PromptVersion=?,UpdateTime=SYSUTCDATETIME()
        WHERE Id=? AND NOT EXISTS(SELECT 1 FROM dbo.ScoringRunResult newer JOIN dbo.ScoringRun nsr ON nsr.Id=newer.ScoringRunId WHERE newer.CandidateJobId=? AND newer.Status='SUCCESS' AND nsr.Id>?);
        UPDATE dbo.AITask SET Status='SUCCESS',ResultJson=?,ErrorMessage=NULL,FinishTime=SYSUTCDATETIME(),LockedUntil=NULL,UpdateTime=SYSUTCDATETIME() WHERE Id=? AND Status='RUNNING';"""
        with pyodbc.connect(build_connection_string(),timeout=30) as connection:
            values=(result["skill_score"],result["project_score"],result["experience_score"],result["education_score"],total,report,str(result.get("strengths") or '')[:4000],str(result.get("weaknesses") or '')[:4000],str(result.get("recommendation") or '')[:1000],model,prompt_version)
            connection.cursor().execute(sql,*values,task.scoring_run_id,task.candidate_job_id,*values[:9],context["score_version"],model,prompt_version,task.candidate_job_id,task.candidate_job_id,task.scoring_run_id,json.dumps({"score":total,"scoringRunId":task.scoring_run_id},ensure_ascii=False),task.task_id)

    def fail_match_score(self,task:MatchScoreTask,message:str)->None:
        with pyodbc.connect(build_connection_string(),timeout=10) as connection:
            connection.cursor().execute("""UPDATE dbo.ScoringRunResult SET Status='FAILED',ErrorMessage=?,FinishTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME() WHERE ScoringRunId=? AND CandidateJobId=?;
            UPDATE dbo.AITask SET Status='FAILED',ErrorMessage=?,FinishTime=SYSUTCDATETIME(),LockedUntil=NULL,UpdateTime=SYSUTCDATETIME() WHERE Id=?;""",message[:2000],task.scoring_run_id,task.candidate_job_id,message[:2000],task.task_id)

    def complete_resume_analysis(self, task: ResumeAnalysisTask, parsed: ParsedResume, model_name: str, prompt_version: str) -> None:
        structured_json = parsed.model_dump_json()
        connection = pyodbc.connect(build_connection_string(), timeout=30, autocommit=False)
        try:
            cursor = connection.cursor()
            current = cursor.execute(
                "SELECT IsCurrent,IsDeleted FROM dbo.Resume WITH (UPDLOCK,HOLDLOCK) WHERE Id=? AND CandidateId=?",
                task.resume_id, task.candidate_id,
            ).fetchone()
            if current is None or bool(current[1]) or not bool(current[0]):
                cursor.execute(
                    "UPDATE dbo.AITask SET Status='CANCELLED',ErrorMessage=N'简历已被更新，旧分析任务取消。',FinishTime=SYSUTCDATETIME(),LockedUntil=NULL,UpdateTime=SYSUTCDATETIME() WHERE Id=? AND Status='RUNNING'",
                    task.task_id,
                )
                connection.commit()
                return
            cursor.execute("DELETE dbo.CandidateSkill WHERE CandidateId=?", task.candidate_id)
            cursor.execute("DELETE dbo.CandidateEducation WHERE CandidateId=?", task.candidate_id)
            cursor.execute("DELETE dbo.CandidateWorkExperience WHERE CandidateId=?", task.candidate_id)
            cursor.execute("DELETE dbo.CandidateProject WHERE CandidateId=?", task.candidate_id)
            cursor.execute("DELETE dbo.CandidateProfile WHERE CandidateId=?", task.candidate_id)
            cursor.execute(
                "INSERT dbo.CandidateProfile(CandidateId,ResumeId,HighestEducation,TotalExperienceMonths,CurrentCompany,CurrentTitle,Summary,StructuredJson,ModelName,PromptVersion) VALUES(?,?,?,?,?,?,?,?,?,?)",
                task.candidate_id, task.resume_id, parsed.highest_education, parsed.total_experience_months,
                parsed.current_company, parsed.current_title, parsed.summary, structured_json, model_name, prompt_version,
            )
            unique_skills = {skill.name.strip().upper(): skill for skill in parsed.skills if skill.name.strip()}
            for skill in unique_skills.values():
                cursor.execute("INSERT dbo.CandidateSkill(CandidateId,SkillName,SkillLevel,ExperienceMonths,Evidence) VALUES(?,?,?,?,?)", task.candidate_id, skill.name, skill.level, skill.experience_months, skill.evidence)
            for item in parsed.educations:
                cursor.execute("INSERT dbo.CandidateEducation(CandidateId,SchoolName,Major,Degree,StartDate,EndDate,Description) VALUES(?,?,?,?,?,?,?)", task.candidate_id, item.school_name, item.major, item.degree, item.start_date, item.end_date, item.description)
            for item in parsed.work_experiences:
                cursor.execute("INSERT dbo.CandidateWorkExperience(CandidateId,CompanyName,JobTitle,StartDate,EndDate,Description,IsCurrent) VALUES(?,?,?,?,?,?,?)", task.candidate_id, item.company_name, item.job_title, item.start_date, item.end_date, item.description, item.is_current)
            for item in parsed.projects:
                cursor.execute("INSERT dbo.CandidateProject(CandidateId,ProjectName,RoleName,Technology,StartDate,EndDate,Description) VALUES(?,?,?,?,?,?,?)", task.candidate_id, item.project_name, item.role_name, item.technology, item.start_date, item.end_date, item.description)
            result = json.dumps({"resumeId": task.resume_id, "model": model_name, "promptVersion": prompt_version}, ensure_ascii=False)
            cursor.execute(
                "UPDATE dbo.Resume SET IsAiAnalyzed=1,ParseStatus='SUCCESS',UpdateTime=SYSUTCDATETIME() WHERE Id=?; "
                "UPDATE dbo.Candidate SET Name=?,Phone=COALESCE(?,Phone),PhoneNormalized=COALESCE(?,PhoneNormalized),"
                "Email=COALESCE(?,Email),EmailNormalized=COALESCE(?,EmailNormalized),UpdateTime=SYSUTCDATETIME() WHERE Id=?; "
                "UPDATE dbo.AITask SET Status='SUCCESS',ResultJson=?,ErrorMessage=NULL,FinishTime=SYSUTCDATETIME(),LockedUntil=NULL,UpdateTime=SYSUTCDATETIME() WHERE Id=? AND Status='RUNNING';",
                task.resume_id, parsed.name, normalize_optional_text(parsed.phone), normalize_phone(parsed.phone), normalize_email(parsed.email),
                normalize_email(parsed.email), task.candidate_id, result, task.task_id,
            )
            connection.commit()
        except Exception:
            connection.rollback()
            raise
        finally:
            connection.close()

    def purge_failed_resume_analysis(self, task: ResumeAnalysisTask) -> int:
        """Remove an unusable candidate completely. RESUME_PARSE failures are not retained or retried."""
        settings = get_settings()
        connection = pyodbc.connect(build_connection_string(), timeout=30, autocommit=False)
        relative_paths: list[str] = []
        try:
            cursor = connection.cursor()
            locked = cursor.execute(
                "SELECT CandidateId FROM dbo.Resume WITH (UPDLOCK,HOLDLOCK) WHERE Id=? AND CandidateId=?",
                task.resume_id, task.candidate_id,
            ).fetchone()
            if locked is None:
                cursor.execute("DELETE dbo.AITask WHERE Id=?", task.task_id)
                connection.commit()
                return 0
            relative_paths = [str(row[0]) for row in cursor.execute(
                "SELECT FilePath FROM dbo.Resume WHERE CandidateId=? AND IsDeleted=0",
                task.candidate_id,
            ).fetchall()]
            cursor.execute(
                """
                SET NOCOUNT ON;
                SELECT Id INTO #R FROM dbo.Resume WHERE CandidateId=?;
                SELECT Id INTO #CJ FROM dbo.CandidateJob WHERE CandidateId=?;
                SELECT DISTINCT ImportFileId INTO #IF FROM dbo.ResumeImportResult WHERE ResumeId IN(SELECT Id FROM #R);
                SELECT DISTINCT BatchId INTO #B FROM dbo.ResumeImportFile WHERE Id IN(SELECT ImportFileId FROM #IF);
                DELETE dbo.CandidateJobReview WHERE CandidateJobId IN(SELECT Id FROM #CJ);
                DELETE dbo.AITask WHERE ResumeId IN(SELECT Id FROM #R) OR CandidateJobId IN(SELECT Id FROM #CJ) OR ResumeImportFileId IN(SELECT ImportFileId FROM #IF);
                DELETE dbo.CleanupLog WHERE ObjectType='RESUME' AND ObjectId IN(SELECT Id FROM #R);
                DELETE dbo.ResumeImportResult WHERE ResumeId IN(SELECT Id FROM #R);
                DELETE dbo.ResumeJobSource WHERE ResumeId IN(SELECT Id FROM #R);
                DELETE dbo.CandidateProfile WHERE CandidateId=?;
                DELETE dbo.CandidateSkill WHERE CandidateId=?;
                DELETE dbo.CandidateEducation WHERE CandidateId=?;
                DELETE dbo.CandidateWorkExperience WHERE CandidateId=?;
                DELETE dbo.CandidateProject WHERE CandidateId=?;
                DELETE dbo.CandidateJob WHERE CandidateId=?;
                DELETE dbo.Resume WHERE CandidateId=?;
                DELETE dbo.Candidate WHERE Id=?;
                DELETE dbo.ResumeImportFile WHERE Id IN(SELECT ImportFileId FROM #IF);
                DELETE dbo.ResumeImportBatchJob WHERE BatchId IN(SELECT BatchId FROM #B)
                  AND NOT EXISTS(SELECT 1 FROM dbo.ResumeImportFile f WHERE f.BatchId=ResumeImportBatchJob.BatchId);
                DELETE dbo.ResumeImportBatch WHERE Id IN(SELECT BatchId FROM #B)
                  AND NOT EXISTS(SELECT 1 FROM dbo.ResumeImportFile f WHERE f.BatchId=ResumeImportBatch.Id);
                UPDATE b SET SourceFileCount=x.TotalFiles,ResumeFileCount=x.TotalResumes,
                  SuccessCount=x.SuccessFiles,FailedCount=x.FailedFiles,UpdateTime=SYSUTCDATETIME()
                FROM dbo.ResumeImportBatch b CROSS APPLY(
                  SELECT COUNT(*) TotalFiles,
                    COALESCE(SUM(CASE WHEN f.DetectedResumeCount IS NULL THEN 0 ELSE f.DetectedResumeCount END),0) TotalResumes,
                    COALESCE(SUM(CASE WHEN f.Status='SUCCESS' THEN 1 ELSE 0 END),0) SuccessFiles,
                    COALESCE(SUM(CASE WHEN f.Status='FAILED' THEN 1 ELSE 0 END),0) FailedFiles
                  FROM dbo.ResumeImportFile f WHERE f.BatchId=b.Id
                ) x WHERE b.Id IN(SELECT BatchId FROM #B);
                """,
                task.candidate_id, task.candidate_id,
                task.candidate_id, task.candidate_id, task.candidate_id, task.candidate_id,
                task.candidate_id, task.candidate_id, task.candidate_id, task.candidate_id,
            )
            connection.commit()
        except Exception:
            connection.rollback()
            raise
        finally:
            connection.close()

        root = settings.resume_root.resolve()
        deleted = 0
        for relative_path in relative_paths:
            path = Path(relative_path)
            if path.is_absolute():
                continue
            full_path = (root / path).resolve()
            if root not in full_path.parents:
                continue
            try:
                full_path.unlink(missing_ok=True)
                deleted += 1
            except OSError:
                pass
        return deleted

    def expire_abandoned_resume_tasks(self) -> int:
        sql = """
        SET NOCOUNT ON;
        UPDATE f SET Status='FAILED',ErrorCode='WORKER_LEASE_EXPIRED',
          ErrorMessage=N'Agent执行中断，任务不自动重试。',UpdateTime=SYSUTCDATETIME()
        FROM dbo.ResumeImportFile f
        JOIN dbo.AITask t ON t.ResumeImportFileId=f.Id
        WHERE t.TaskType IN ('RESUME_IMPORT','MANUAL_RESUME_IMPORT') AND t.Status='RUNNING' AND t.LockedUntil<SYSUTCDATETIME();
        DECLARE @Count INT; SET @Count=@@ROWCOUNT;
        UPDATE dbo.AITask SET Status='FAILED',ErrorMessage=N'Agent租约过期，简历导入不自动重试。',
          FinishTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME(),LockedUntil=NULL
        WHERE TaskType IN ('RESUME_IMPORT','MANUAL_RESUME_IMPORT') AND Status='RUNNING' AND LockedUntil<SYSUTCDATETIME();
        UPDATE b SET ResumeFileCount=x.ResumeCount,SuccessCount=x.SuccessCount,FailedCount=x.FailedCount,
          Status=CASE WHEN x.ActiveCount>0 THEN 'PROCESSING' WHEN x.FailedCount=0 THEN 'SUCCESS'
                      WHEN x.SuccessCount=0 THEN 'FAILED' ELSE 'PARTIAL_SUCCESS' END,
          FinishTime=CASE WHEN x.ActiveCount=0 THEN SYSUTCDATETIME() ELSE NULL END,UpdateTime=SYSUTCDATETIME()
        FROM dbo.ResumeImportBatch b CROSS APPLY(
          SELECT COALESCE(SUM(CASE WHEN f.DetectedResumeCount IS NULL THEN 0 ELSE f.DetectedResumeCount END),0) ResumeCount,
            SUM(CASE WHEN f.Status='SUCCESS' THEN 1 ELSE 0 END) SuccessCount,
            SUM(CASE WHEN f.Status IN ('FAILED','REJECTED') THEN 1 ELSE 0 END) FailedCount,
            SUM(CASE WHEN f.Status IN ('QUEUED','PROCESSING','SPLIT') THEN 1 ELSE 0 END) ActiveCount
          FROM dbo.ResumeImportFile f WHERE f.BatchId=b.Id
        ) x WHERE b.Status='PROCESSING';
        SELECT @Count;
        """
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            row = connection.cursor().execute(sql).fetchone()
            return int(row[0]) if row else 0

    def claim_resume_import(self) -> ImportTask | None:
        return self._claim_resume_import("RESUME_IMPORT")

    def claim_manual_resume_import(self) -> ImportTask | None:
        return self._claim_resume_import("MANUAL_RESUME_IMPORT")

    def _claim_resume_import(self, task_type: str) -> ImportTask | None:
        settings = get_settings()
        sql = """
        SET NOCOUNT ON;
        DECLARE @Claimed TABLE(TaskId BIGINT,ImportFileId BIGINT);
        ;WITH NextTask AS (
          SELECT TOP (1) * FROM dbo.AITask WITH (UPDLOCK,READPAST,ROWLOCK)
          WHERE TaskType=? AND Status='WAITING' AND NextRunTime<=SYSUTCDATETIME()
          ORDER BY Priority DESC,CreateTime ASC
        )
        UPDATE NextTask SET Status='RUNNING',WorkerId=?,LockedUntil=DATEADD(MINUTE,?,SYSUTCDATETIME()),
          StartTime=COALESCE(StartTime,SYSUTCDATETIME()),UpdateTime=SYSUTCDATETIME()
        OUTPUT inserted.Id,inserted.ResumeImportFileId INTO @Claimed;
        UPDATE f SET Status='PROCESSING',UpdateTime=SYSUTCDATETIME()
        FROM dbo.ResumeImportFile f JOIN @Claimed c ON c.ImportFileId=f.Id;
        UPDATE b SET Status='PROCESSING',UpdateTime=SYSUTCDATETIME()
        FROM dbo.ResumeImportBatch b JOIN dbo.ResumeImportFile f ON f.BatchId=b.Id
        JOIN @Claimed c ON c.ImportFileId=f.Id WHERE b.Status='QUEUED';
        SELECT c.TaskId,f.Id,f.BatchId,CONVERT(VARCHAR(36),b.BatchKey),f.SourceType,f.OriginalFileName,
          f.FileExtension,f.TempFilePath,f.FileHash
        FROM @Claimed c JOIN dbo.ResumeImportFile f ON f.Id=c.ImportFileId
        JOIN dbo.ResumeImportBatch b ON b.Id=f.BatchId;
        """
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            cursor = connection.cursor()
            row = cursor.execute(sql, task_type, settings.worker_id, settings.worker_lease_minutes).fetchone()
            if row is None: return None
            return ImportTask(
                task_id=row[0], import_file_id=row[1], batch_id=row[2], batch_key=row[3],
                source_type=row[4], original_file_name=row[5], file_extension=row[6],
                temp_file_path=row[7], file_hash=row[8], worker_id=settings.worker_id,
            )

    def get_batch_job_ids(self, batch_id: int) -> list[int]:
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            return [int(row[0]) for row in connection.cursor().execute(
                "SELECT JobId FROM dbo.ResumeImportBatchJob WHERE BatchId=? ORDER BY JobId", batch_id
            ).fetchall()]

    def get_import_file_job_sources(self, import_file_id: int, batch_id: int) -> list[tuple[int, int | None]]:
        """Use the external source job on a JSON file; preserve manual batch jobs as a fallback."""
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            row = connection.cursor().execute(
                "SELECT SourceJobId,ExternalJobMappingId FROM dbo.ResumeImportFile WHERE Id=?", import_file_id
            ).fetchone()
            if row and row[0] is not None:
                return [(int(row[0]), int(row[1]) if row[1] is not None else None)]
        return [(job_id, None) for job_id in self.get_batch_job_ids(batch_id)]

    def save_prepared_resume(
        self, task: ImportTask, prepared: PreparedResume, parsed: ParsedResume,
        formal_relative_path: str, model_name: str, prompt_version: str, queue_analysis: bool = True,
    ) -> tuple[int, int]:
        raw_phone = normalize_optional_text(parsed.phone)
        phone = normalize_phone(parsed.phone)
        email = normalize_email(parsed.email)
        structured_json = parsed.model_dump_json()
        connection = pyodbc.connect(build_connection_string(), timeout=30, autocommit=False)
        try:
            cursor = connection.cursor()
            duplicate = cursor.execute(
                "SELECT TOP (1) r.Id,r.CandidateId FROM dbo.Resume r WITH (UPDLOCK,HOLDLOCK) WHERE r.FileHash=? AND r.IsDeleted=0",
                prepared.file_hash,
            ).fetchone()
            if duplicate:
                raise ResumeImportError("DUPLICATE_RESUME", "正式简历库已存在相同文件。")

            matches = cursor.execute(
                "SELECT Id FROM dbo.Candidate WITH (UPDLOCK,HOLDLOCK) WHERE IsDeleted=0 AND "
                "((? IS NOT NULL AND PhoneNormalized=?) OR (? IS NOT NULL AND EmailNormalized=?)) ORDER BY Id",
                phone, phone, email, email,
            ).fetchall()
            candidate_ids = {int(row[0]) for row in matches}
            if prepared.talent_id:
                source_matches = cursor.execute(
                    "SELECT DISTINCT CandidateId FROM dbo.Resume WITH (UPDLOCK,HOLDLOCK) "
                    "WHERE IsDeleted=0 AND ResumeSource=? AND TalentId=?",
                    prepared.resume_source, prepared.talent_id,
                ).fetchall()
                candidate_ids.update(int(row[0]) for row in source_matches)
            if len(candidate_ids) > 1:
                raise ResumeImportError("CANDIDATE_IDENTITY_CONFLICT", "人才ID、手机号或邮箱命中了不同候选人。")
            if candidate_ids:
                candidate_id = next(iter(candidate_ids))
                cursor.execute(
                    "UPDATE dbo.Candidate SET Name=?,Phone=COALESCE(?,Phone),PhoneNormalized=COALESCE(?,PhoneNormalized),"
                    "Email=COALESCE(?,Email),EmailNormalized=COALESCE(?,EmailNormalized),"
                    "Source=CASE WHEN Source='HR_UPLOAD' AND ?='51JOB' THEN '51JOB' ELSE Source END,UpdateTime=SYSUTCDATETIME() WHERE Id=?",
                    parsed.name, raw_phone, phone, email, email, prepared.resume_source, candidate_id,
                )
            else:
                candidate_row = cursor.execute(
                    "SET NOCOUNT ON; INSERT dbo.Candidate(Name,Phone,PhoneNormalized,Email,EmailNormalized,Source,Status,IsDeleted) "
                    "VALUES(?,?,?,?,?,?,'NEW',0); SELECT CAST(SCOPE_IDENTITY() AS BIGINT)",
                    parsed.name, raw_phone, phone, email, email, prepared.resume_source,
                ).fetchone()
                candidate_id = int(candidate_row[0])

            cursor.execute("UPDATE dbo.Resume SET IsCurrent=0,UpdateTime=SYSUTCDATETIME() WHERE CandidateId=? AND IsCurrent=1 AND IsDeleted=0", candidate_id)
            cursor.execute(
                "UPDATE t SET Status='CANCELLED',ErrorMessage=N'简历已产生新版本，旧版本解析任务自动取消。',FinishTime=SYSUTCDATETIME(),WorkerId=NULL,LockedUntil=NULL,UpdateTime=SYSUTCDATETIME() "
                "FROM dbo.AITask t JOIN dbo.Resume r ON r.Id=t.ResumeId WHERE r.CandidateId=? AND t.TaskType='RESUME_PARSE' AND t.Status='WAITING' AND r.IsCurrent=0",
                candidate_id,
            )
            resume_row = cursor.execute(
                "SET NOCOUNT ON; INSERT dbo.Resume(CandidateId,FileName,FileExtension,FilePath,FileSize,FileHash,RawText,ParseStatus,IsCurrent,IsDeleted,OriginalFileSize,IsOptimized,TalentId,ResumeSource,IsAiAnalyzed) "
                "VALUES(?,?,?,?,?,?,?,'SUCCESS',1,0,?,?,?,?,?); SELECT CAST(SCOPE_IDENTITY() AS BIGINT)",
                candidate_id, task.original_file_name, task.file_extension, formal_relative_path,
                prepared.final_size, prepared.file_hash, prepared.text, prepared.original_size, prepared.is_optimized,
                prepared.talent_id, prepared.resume_source, 0 if queue_analysis else 1,
            ).fetchone()
            resume_id = int(resume_row[0])
            cursor.execute("DELETE dbo.CandidateSkill WHERE CandidateId=?", candidate_id)
            cursor.execute("DELETE dbo.CandidateEducation WHERE CandidateId=?", candidate_id)
            cursor.execute("DELETE dbo.CandidateWorkExperience WHERE CandidateId=?", candidate_id)
            cursor.execute("DELETE dbo.CandidateProject WHERE CandidateId=?", candidate_id)
            cursor.execute("DELETE dbo.CandidateProfile WHERE CandidateId=?", candidate_id)
            cursor.execute(
                "INSERT dbo.CandidateProfile(CandidateId,ResumeId,HighestEducation,TotalExperienceMonths,CurrentCompany,CurrentTitle,Summary,StructuredJson,ModelName,PromptVersion) "
                "VALUES(?,?,?,?,?,?,?,?,?,?)",
                candidate_id, resume_id, parsed.highest_education, parsed.total_experience_months,
                parsed.current_company, parsed.current_title, parsed.summary, structured_json, model_name, prompt_version,
            )
            unique_skills = {skill.name.strip().upper(): skill for skill in parsed.skills if skill.name.strip()}
            for skill in unique_skills.values():
                cursor.execute(
                    "INSERT dbo.CandidateSkill(CandidateId,SkillName,SkillLevel,ExperienceMonths,Evidence) VALUES(?,?,?,?,?)",
                    candidate_id, skill.name, skill.level, skill.experience_months, skill.evidence,
                )
            for item in parsed.educations:
                cursor.execute(
                    "INSERT dbo.CandidateEducation(CandidateId,SchoolName,Major,Degree,StartDate,EndDate,Description) VALUES(?,?,?,?,?,?,?)",
                    candidate_id, item.school_name, item.major, item.degree, item.start_date, item.end_date, item.description,
                )
            for item in parsed.work_experiences:
                cursor.execute(
                    "INSERT dbo.CandidateWorkExperience(CandidateId,CompanyName,JobTitle,StartDate,EndDate,Description,IsCurrent) VALUES(?,?,?,?,?,?,?)",
                    candidate_id, item.company_name, item.job_title, item.start_date, item.end_date, item.description, item.is_current,
                )
            for item in parsed.projects:
                cursor.execute(
                    "INSERT dbo.CandidateProject(CandidateId,ProjectName,RoleName,Technology,StartDate,EndDate,Description) VALUES(?,?,?,?,?,?,?)",
                    candidate_id, item.project_name, item.role_name, item.technology, item.start_date, item.end_date, item.description,
                )
            for job_id, mapping_id in self.get_import_file_job_sources(task.import_file_id, task.batch_id):
                if mapping_id is not None:
                    cursor.execute(
                        "IF NOT EXISTS(SELECT 1 FROM dbo.ResumeJobSource WHERE ResumeId=? AND ExternalJobMappingId=?) "
                        "INSERT dbo.ResumeJobSource(ResumeId,JobId,ExternalJobMappingId) VALUES(?,?,?)",
                        resume_id, mapping_id, resume_id, job_id, mapping_id,
                    )
                row = cursor.execute("SELECT Id FROM dbo.CandidateJob WITH (UPDLOCK,HOLDLOCK) WHERE CandidateId=? AND JobId=?", candidate_id, job_id).fetchone()
                if row:
                    candidate_job_id = int(row[0])
                    cursor.execute("UPDATE dbo.CandidateJob SET Status='PENDING',UpdateTime=SYSUTCDATETIME() WHERE Id=?", candidate_job_id)
                else:
                    candidate_job_row = cursor.execute(
                        "SET NOCOUNT ON; INSERT dbo.CandidateJob(CandidateId,JobId,Status) VALUES(?,?,'PENDING'); "
                        "SELECT CAST(SCOPE_IDENTITY() AS BIGINT)", candidate_id, job_id
                    ).fetchone()
                    candidate_job_id = int(candidate_job_row[0])
            if queue_analysis:
                parse_business_key = f"resume-parse:{resume_id}"
                cursor.execute(
                    "IF NOT EXISTS(SELECT 1 FROM dbo.AITask WHERE TaskType='RESUME_PARSE' AND ResumeId=?) "
                    "INSERT dbo.AITask(TaskType,ResumeId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey) "
                    "VALUES('RESUME_PARSE',?,'WAITING',4,0,3,?)",
                    resume_id, resume_id, parse_business_key,
                )
            _insert_result(cursor, task.import_file_id, resume_id, prepared)
            cursor.execute(
                "INSERT dbo.CleanupLog(ObjectType,ObjectId,Action,Reason,DetailJson) VALUES('RESUME_IMPORT_FILE',?,'IMPORT_SUCCESS',N'简历正式导入成功',?)",
                task.import_file_id, json.dumps({"resumeId": resume_id, "sequenceNo": prepared.sequence_no}, ensure_ascii=False),
            )
            connection.commit()
            return candidate_id, resume_id
        except Exception:
            connection.rollback()
            raise
        finally:
            connection.close()

    def complete_resume_import(self, task: ImportTask, resume_count: int) -> None:
        sql = """
        UPDATE dbo.ResumeImportFile SET Status='SUCCESS',DetectedResumeCount=?,ErrorCode=NULL,ErrorMessage=NULL,UpdateTime=SYSUTCDATETIME()
        WHERE Id=? AND Status IN ('PROCESSING','SPLIT');
        UPDATE dbo.AITask SET Status='SUCCESS',FinishTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME(),LockedUntil=NULL,ErrorMessage=NULL
        WHERE Id=? AND Status='RUNNING';
        """
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            connection.cursor().execute(sql, resume_count, task.import_file_id, task.task_id)
            _recount_batch(connection.cursor(), task.batch_id)

    def mark_split(self, task: ImportTask, resume_count: int) -> None:
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            connection.cursor().execute(
                "UPDATE dbo.ResumeImportFile SET Status='SPLIT',DetectedResumeCount=?,UpdateTime=SYSUTCDATETIME() WHERE Id=? AND Status='PROCESSING'",
                resume_count, task.import_file_id,
            )

    def fail_resume_import(self, task: ImportTask, code: str, message: str, completed_count: int = 0, failed_relative_path: str | None = None) -> None:
        safe_message = message[:2000]
        sql = """
        UPDATE dbo.ResumeImportFile SET Status='FAILED',DetectedResumeCount=CASE WHEN ?>0 THEN ? ELSE DetectedResumeCount END,
          TempFilePath=COALESCE(?,TempFilePath),ErrorCode=?,ErrorMessage=?,UpdateTime=SYSUTCDATETIME() WHERE Id=?;
        UPDATE dbo.AITask SET Status='FAILED',ErrorMessage=?,FinishTime=SYSUTCDATETIME(),UpdateTime=SYSUTCDATETIME(),LockedUntil=NULL
        WHERE Id=?;
        """
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            cursor = connection.cursor()
            cursor.execute(sql, completed_count, completed_count, failed_relative_path, code[:100], safe_message, task.import_file_id, safe_message, task.task_id)
            _recount_batch(cursor, task.batch_id)

    def record_cleanup_failure(self, task: ImportTask) -> None:
        with pyodbc.connect(build_connection_string(), timeout=10) as connection:
            connection.cursor().execute(
                "INSERT dbo.CleanupLog(ObjectType,ObjectId,Action,Reason,DetailJson) "
                "VALUES('RESUME_IMPORT_FILE',?,'TEMP_DELETE_FAILED',N'业务已成功，但临时文件清理失败，将由运维人工处理。',NULL)",
                task.import_file_id,
            )


def normalize_phone(value: str | None) -> str | None:
    if not value or not value.strip(): return None
    normalized = value.translate(str.maketrans("", "", " -()（）"))
    return normalized or None


def normalize_email(value: str | None) -> str | None:
    return None if not value or not value.strip() else value.strip().lower()


def normalize_optional_text(value: str | None) -> str | None:
    return None if not value or not value.strip() else value.strip()


def _insert_result(cursor: pyodbc.Cursor, import_file_id: int, resume_id: int, prepared: PreparedResume) -> None:
    cursor.execute(
        "IF NOT EXISTS(SELECT 1 FROM dbo.ResumeImportResult WHERE ImportFileId=? AND SequenceNo=?) "
        "INSERT dbo.ResumeImportResult(ImportFileId,ResumeId,SequenceNo,SourcePageStart,SourcePageEnd) VALUES(?,?,?,?,?)",
        import_file_id, prepared.sequence_no, import_file_id, resume_id, prepared.sequence_no,
        prepared.source_page_start, prepared.source_page_end,
    )


def _recount_batch(cursor: pyodbc.Cursor, batch_id: int) -> None:
    cursor.execute(
        """
        UPDATE b SET ResumeFileCount=x.ResumeCount,SuccessCount=x.SuccessCount,FailedCount=x.FailedCount,
          Status=CASE WHEN x.ActiveCount>0 THEN 'PROCESSING' WHEN x.FailedCount=0 THEN 'SUCCESS'
                      WHEN x.SuccessCount=0 THEN 'FAILED' ELSE 'PARTIAL_SUCCESS' END,
          FinishTime=CASE WHEN x.ActiveCount=0 THEN SYSUTCDATETIME() ELSE NULL END,UpdateTime=SYSUTCDATETIME()
        FROM dbo.ResumeImportBatch b CROSS APPLY(
          SELECT COALESCE(SUM(CASE WHEN f.DetectedResumeCount IS NULL THEN 0 ELSE f.DetectedResumeCount END),0) ResumeCount,
            SUM(CASE WHEN f.Status='SUCCESS' THEN 1 ELSE 0 END) SuccessCount,
            SUM(CASE WHEN f.Status IN ('FAILED','REJECTED') THEN 1 ELSE 0 END) FailedCount,
            SUM(CASE WHEN f.Status IN ('QUEUED','PROCESSING','SPLIT') THEN 1 ELSE 0 END) ActiveCount
          FROM dbo.ResumeImportFile f WHERE f.BatchId=b.Id
        ) x WHERE b.Id=?
        """, batch_id,
    )
