from app.task_worker.repository import normalize_email, normalize_optional_text, normalize_phone


def test_blank_contacts_are_normalized_to_none() -> None:
    for value in (None, "", "   "):
        assert normalize_email(value) is None
        assert normalize_optional_text(value) is None
        assert normalize_phone(value) is None


def test_contact_values_are_trimmed_and_normalized() -> None:
    assert normalize_email("  User@Example.COM  ") == "user@example.com"
    assert normalize_optional_text("  138 0000 0000  ") == "138 0000 0000"
    assert normalize_phone(" 138 0000 0000 ") == "13800000000"
