"""Backfill deterministic JSON profile fields without calling an AI service.

Only profiles created by the LOCAL_JSON importer are changed. Existing AI-parsed
profiles and HR-maintained data are deliberately left unchanged.
"""
from __future__ import annotations

from app.database import build_connection_string
from app.resume_agent.json_source import build_local_json_profile
import pyodbc


def main() -> None:
    updated_profiles = 0
    corrected_names = 0
    query = """
        SELECT c.Id,c.Name,r.Id,r.RawText
        FROM dbo.Candidate c
        JOIN dbo.Resume r ON r.CandidateId=c.Id AND r.IsCurrent=1 AND r.IsDeleted=0
        JOIN dbo.CandidateProfile p ON p.CandidateId=c.Id AND p.ResumeId=r.Id
        WHERE r.FileExtension='JSON' AND p.ModelName='LOCAL_JSON' AND r.RawText IS NOT NULL
    """
    with pyodbc.connect(build_connection_string(), timeout=30, autocommit=False) as connection:
        cursor = connection.cursor()
        records = cursor.execute(query).fetchall()
        for candidate_id, current_name, resume_id, raw_text in records:
            profile = build_local_json_profile(str(current_name), str(raw_text))
            cursor.execute(
                "UPDATE dbo.CandidateProfile SET HighestEducation=?,TotalExperienceMonths=?,CurrentTitle=?,"
                "Summary=NULL,StructuredJson=?,ModelName='LOCAL_JSON',PromptVersion='json-local-v1',"
                "UpdateTime=SYSUTCDATETIME() WHERE CandidateId=? AND ResumeId=?",
                profile.highest_education, profile.total_experience_months, profile.current_title,
                profile.model_dump_json(), candidate_id, resume_id,
            )
            updated_profiles += 1
            if str(current_name) != profile.name:
                cursor.execute("UPDATE dbo.Candidate SET Name=?,UpdateTime=SYSUTCDATETIME() WHERE Id=?", profile.name, candidate_id)
                corrected_names += 1
        connection.commit()
    print(f"UpdatedLocalJsonProfiles={updated_profiles}; CorrectedNames={corrected_names}")


if __name__ == "__main__":
    main()
