from pathlib import Path


def test_match_score_claims_newest_resume_first_within_oldest_run() -> None:
    repository_source = (
        Path(__file__).resolve().parents[1] / "app" / "task_worker" / "repository.py"
    ).read_text(encoding="utf-8")

    claim_start = repository_source.index("def claim_match_score")
    claim_end = repository_source.index("def load_match_context", claim_start)
    claim_source = repository_source[claim_start:claim_end]

    assert "JOIN dbo.ScoringRun sr ON sr.Id=t.ScoringRunId" in claim_source
    assert "JOIN dbo.ScoringRunResult rr ON rr.ScoringRunId=t.ScoringRunId" in claim_source
    assert (
        "ORDER BY t.Priority DESC,sr.CreateTime ASC,sr.Id ASC,\n"
        "            rr.ResumeImportTime DESC,rr.ResumeId DESC,"
        "t.CandidateJobId DESC,t.Id DESC"
    ) in claim_source
