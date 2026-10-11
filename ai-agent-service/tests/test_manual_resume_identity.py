from app.manual_resume_agent.identity import detect_manual_resume_identity
from pathlib import Path


def test_detects_51job_talent_id_from_manual_pdf_file_name() -> None:
    result = detect_manual_resume_identity(
        "51job_高贺晨_电驱总成设计_嘉兴(744863065).pdf",
        "候选人简历正文",
    )
    assert result == ("744863065", "51JOB")


def test_detects_labeled_51job_talent_id_from_text() -> None:
    result = detect_manual_resume_identity(
        "51job_候选人简历.pdf",
        "前程无忧\n人才ID：744863065\n姓名：高先生",
    )
    assert result == ("744863065", "51JOB")


def test_does_not_treat_unlabeled_phone_as_talent_id() -> None:
    result = detect_manual_resume_identity("普通人工简历.pdf", "联系电话：13800138000")
    assert result == (None, "HR_UPLOAD")


def test_repository_marks_already_parsed_manual_resume_as_ai_analyzed() -> None:
    source = (Path(__file__).parents[1] / "app" / "task_worker" / "repository.py").read_text(encoding="utf-8")
    assert "TalentId,ResumeSource,IsAiAnalyzed" in source
    assert "0 if queue_analysis else 1" in source
