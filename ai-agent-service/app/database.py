import pyodbc

from config.settings import get_settings


def build_connection_string() -> str:
    settings = get_settings()
    parts = [
        f"DRIVER={{{settings.db_driver}}}",
        f"SERVER={settings.db_server}",
        f"DATABASE={settings.db_database}",
        "Encrypt=no",
        "TrustServerCertificate=yes",
    ]

    if settings.db_trusted_connection:
        parts.append("Trusted_Connection=yes")
    else:
        if not settings.db_user or not settings.db_password:
            raise ValueError("SQL authentication requires DB_USER and DB_PASSWORD")
        parts.extend(
            [
                f"UID={settings.db_user}",
                f"PWD={settings.db_password.get_secret_value()}",
            ]
        )

    return ";".join(parts) + ";"


def check_database() -> dict[str, str | int]:
    with pyodbc.connect(build_connection_string(), timeout=5) as connection:
        cursor = connection.cursor()
        row = cursor.execute(
            "SELECT DB_NAME(), CAST(SERVERPROPERTY('ProductVersion') AS varchar(30)), "
            "(SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0)"
        ).fetchone()
        return {
            "database": row[0],
            "server_version": row[1],
            "table_count": row[2],
        }
