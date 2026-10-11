from functools import lru_cache
from pathlib import Path

from pydantic import SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


ENV_FILE = Path(__file__).resolve().parents[1] / ".env"


class Settings(BaseSettings):
    app_env: str = "development"
    app_host: str = "127.0.0.1"
    app_port: int = 8100

    deepseek_api_key: SecretStr
    deepseek_base_url: str = "https://api.deepseek.com"
    deepseek_model: str = "deepseek-v4-flash"

    db_driver: str = "ODBC Driver 17 for SQL Server"
    db_server: str
    db_database: str = "RecruitAI"
    db_trusted_connection: bool = True
    db_user: str | None = None
    db_password: SecretStr | None = None

    resume_root: Path = Path(r"C:\Zhaopin\Resume")
    resume_temp_root: Path = Path(r"C:\Zhaopin\ResumeTemp")
    worker_enabled: bool = True
    worker_id: str = "agent-local-1"
    worker_poll_seconds: int = 5
    worker_concurrency: int = 10
    worker_lease_minutes: int = 10
    resume_target_file_bytes: int = 20 * 1024 * 1024
    resume_hard_source_file_bytes: int = 500 * 1024 * 1024
    resume_json_max_file_bytes: int = 10 * 1024 * 1024
    deepseek_timeout_seconds: int = 120
    deepseek_max_tokens: int = 5000
    rag_embedding_model: str = "hashing-char-ngram-v1"
    rag_embedding_dimension: int = 1024
    rag_chunk_chars: int = 1200
    rag_chunk_overlap_chars: int = 160

    model_config = SettingsConfigDict(
        env_file=ENV_FILE,
        env_file_encoding="utf-8",
        case_sensitive=False,
        extra="ignore",
    )


@lru_cache
def get_settings() -> Settings:
    return Settings()
