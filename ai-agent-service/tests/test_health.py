from main import health


def test_health() -> None:
    result = health()
    assert result["status"] == "ok"
