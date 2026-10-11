# RecruitAI 数据库迁移

当前增量脚本：

- `V002__web_import_review_knowledge.sql`：Web导入、人工复核和知识库；
- `V003__resume_json_import.sql`：JSON简历导入，并为 `dbo.Resume` 增加 `TalentId`、`ResumeSource` 与来源人才查询索引。
- `V010__job_profile_management.sql`：岗位与版本化岗位画像关联；迁移现有岗位要求并为JSON岗位补建画像草稿。
- `V011__resume_favorites.sql`：在候选人–岗位关系上增加共享简历收藏、收藏人和收藏时间。
- `V012__candidate_contact_null_normalization.sql`：清理候选人联系方式历史空字符串，统一为空值 `NULL`。
- `V013__ai_voice_interview_mini_program.sql`：微信小程序AI语音面试完整增量；新增18张业务表、HR列表视图，并扩展AITask支持候选人专属题集和面试报告任务。
- `V014__scoring_run_history.sql`：支持重复启动岗位评分；保存每次评分参数、候选人结果和历史排行，并关联AI评分任务。
- `V015__interview_invitation_delivery.sql`：记录面试邀请邮件、短信逐渠道投递状态、目标掩码、服务商流水号和失败原因。
- `V016__interview_delete_and_report_submission.sql`：支持面试邀请业务软删除，并为提交后生成的报告保存简历关键信息和语音面试信息。
- `V017__manual_resume_import.sql`：恢复 PDF/DOCX 人工简历来源约束，并新增独立的 `MANUAL_RESUME_IMPORT` Agent 任务类型。
- `V018__job_candidate_cleanup_rules.sql`：为岗位增加年龄及期望月薪上限，用于按岗位一键清理超限候选人。
- `V019__job_candidate_cleanup_history.sql`：记录一键清理前的岗位候选人状态，支持精确恢复且不影响人工淘汰记录。

执行前提：

- 数据库已经完成V001的17张基础表；
- SQL Server版本为2008 R2，数据库兼容级别为100；
- 执行账号仅在维护窗口临时取得RecruitAI数据库DDL权限；
- 执行前完成数据库备份；
- Web和Agent尚未启动或已经停止领取任务。

开发环境执行命令：

```powershell
sqlcmd -S "PC-HOMEC\SQLEXPRESS" -d RecruitAI -E -b -i ".\database\V002__web_import_review_knowledge.sql"
sqlcmd -S "PC-HOMEC\SQLEXPRESS" -d RecruitAI -E -b -i ".\database\V003__resume_json_import.sql"
sqlcmd -S "PC-HOMEC\SQLEXPRESS" -d RecruitAI -E -b -i ".\database\V013__ai_voice_interview_mini_program.sql"
sqlcmd -S "PC-HOMEC\SQLEXPRESS" -d RecruitAI -E -b -f 65001 -i ".\database\V014__scoring_run_history.sql"
sqlcmd -S "PC-HOMEC\SQLEXPRESS" -d RecruitAI -E -b -f 65001 -i ".\database\V015__interview_invitation_delivery.sql"
sqlcmd -S "PC-HOMEC\SQLEXPRESS" -d RecruitAI -E -b -f 65001 -i ".\database\V016__interview_delete_and_report_submission.sql"
sqlcmd -S "PC-HOMEC\SQLEXPRESS" -d RecruitAI -E -b -f 65001 -i ".\database\V017__manual_resume_import.sql"
sqlcmd -S "PC-HOMEC\SQLEXPRESS" -d RecruitAI -E -b -f 65001 -i ".\database\V018__job_candidate_cleanup_rules.sql"
sqlcmd -S "PC-HOMEC\SQLEXPRESS" -d RecruitAI -E -b -f 65001 -i ".\database\V019__job_candidate_cleanup_history.sql"
```

V013执行成功后应返回`InterviewTableCount=18`，且`MissingRequiredObject`为空。随后启动Web，启动初始化程序只会在不存在ADMIN用户时创建 `admin`，初始密码为 `123456`，数据库中只保存ASP.NET Core PasswordHasher产生的哈希。

V003已于开发环境真实执行并通过结构复查。生产环境执行前仍须备份，并确保Web与Agent停止领取任务。

当前开发库复查结果：V002～V017已执行；V014、V015、V017均已连续执行两次验证幂等性，V014将原有排行迁移为第1次历史排行。生产部署前仍须备份，并停止Web、Agent Worker和Interview Gateway写入。
