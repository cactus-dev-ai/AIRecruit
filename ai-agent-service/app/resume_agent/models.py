from __future__ import annotations

from datetime import date

from pydantic import BaseModel, ConfigDict, Field, field_validator


class Skill(BaseModel):
    name: str = Field(min_length=1, max_length=100)
    level: str | None = Field(default=None, max_length=20)
    experience_months: int | None = Field(default=None, ge=0, le=720)
    evidence: str | None = Field(default=None, max_length=1000)


class Education(BaseModel):
    school_name: str = Field(min_length=1, max_length=200)
    major: str | None = Field(default=None, max_length=200)
    degree: str | None = Field(default=None, max_length=50)
    start_date: date | None = None
    end_date: date | None = None
    description: str | None = Field(default=None, max_length=1000)


class WorkExperience(BaseModel):
    company_name: str = Field(min_length=1, max_length=200)
    job_title: str | None = Field(default=None, max_length=200)
    start_date: date | None = None
    end_date: date | None = None
    description: str | None = Field(default=None, max_length=5000)
    is_current: bool = False


class Project(BaseModel):
    project_name: str = Field(min_length=1, max_length=200)
    role_name: str | None = Field(default=None, max_length=200)
    technology: str | None = Field(default=None, max_length=1000)
    start_date: date | None = None
    end_date: date | None = None
    description: str | None = Field(default=None, max_length=5000)


class ParsedResume(BaseModel):
    model_config = ConfigDict(extra="forbid")

    name: str = Field(min_length=1, max_length=100)
    phone: str | None = Field(default=None, max_length=30)
    email: str | None = Field(default=None, max_length=254)
    highest_education: str | None = Field(default=None, max_length=50)
    total_experience_months: int | None = Field(default=None, ge=0, le=720)
    current_company: str | None = Field(default=None, max_length=200)
    current_title: str | None = Field(default=None, max_length=200)
    summary: str | None = Field(default=None, max_length=5000)
    skills: list[Skill] = Field(default_factory=list, max_length=200)
    educations: list[Education] = Field(default_factory=list, max_length=50)
    work_experiences: list[WorkExperience] = Field(default_factory=list, max_length=100)
    projects: list[Project] = Field(default_factory=list, max_length=100)

    @field_validator("name", "phone", "email", "highest_education", "current_company", "current_title", "summary", mode="before")
    @classmethod
    def trim_strings(cls, value: object) -> object:
        return value.strip() if isinstance(value, str) else value


class ImportTask(BaseModel):
    task_id: int
    import_file_id: int
    batch_id: int
    batch_key: str
    source_type: str
    original_file_name: str
    file_extension: str
    temp_file_path: str
    file_hash: str
    worker_id: str


class ResumeAnalysisTask(BaseModel):
    task_id: int
    resume_id: int
    candidate_id: int
    raw_text: str


class MatchScoreTask(BaseModel):
    task_id: int
    scoring_run_id: int
    candidate_job_id: int
    candidate_id: int
    job_id: int


class SplitResumeFile(BaseModel):
    path: str
    sequence_no: int
    source_page_start: int | None = None
    source_page_end: int | None = None


class PreparedResume(BaseModel):
    path: str
    sequence_no: int
    source_page_start: int | None = None
    source_page_end: int | None = None
    text: str
    original_size: int
    final_size: int
    file_hash: str
    is_optimized: bool
    talent_id: str | None = None
    resume_source: str
