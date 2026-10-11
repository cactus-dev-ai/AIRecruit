from __future__ import annotations

from app.deepseek_client import _build_knowledge_draft_prompt, _deduplicate_instruction, _job_draft_is_weak, _normalize_resume_payload
from app.resume_agent.models import ParsedResume


def test_deepseek_aliases_and_month_dates_are_normalized() -> None:
    payload = {
        "name": "测试候选人", "phone": None, "email": None,
        "educations": [{"school": "测试大学", "field_of_study": "机械", "start_date": "2010-09", "end_date": "2014-07"}],
        "work_experiences": [{"company": "测试公司", "title": "工程师", "start_date": "2014-07", "end_date": "至今", "description": None}],
        "projects": [{"name": "测试项目", "role": "负责人", "technologies": ["Python", "SQL"], "start_date": "2020", "end_date": None}],
        "skills": [{"skill_name": "Python", "level": "熟练"}],
    }
    parsed = ParsedResume.model_validate(_normalize_resume_payload(payload))
    assert parsed.educations[0].school_name == "测试大学"
    assert str(parsed.educations[0].start_date) == "2010-09-01"
    assert parsed.work_experiences[0].company_name == "测试公司"
    assert parsed.work_experiences[0].end_date is None
    assert parsed.projects[0].technology == "Python, SQL"
    assert parsed.skills[0].name == "Python"
    assert parsed.skills[0].level == "INTERMEDIATE"


def test_job_profile_prompt_requests_actionable_content() -> None:
    prompt = _build_knowledge_draft_prompt(
        "装修设计师", "JOB_REQUIREMENT", "岗位画像待HR完善", "室内装修；室内装修行业；绘画CAD",
    )
    assert "通用公开知识" in prompt
    assert "全文最多出现2次" in prompt
    assert "主要职责、核心技能、工具与交付物各至少列出3条" in prompt
    assert "目标长度700至1800" in prompt


def test_duplicate_job_profile_instructions_are_removed() -> None:
    assert _deduplicate_instruction("室内装修；室内装修行业；室内装修行业 绘画CAD") == "室内装修；室内装修行业；绘画CAD"


def test_pending_only_job_profile_is_weak() -> None:
    weak = "岗位定位：【待HR确认】\n主要职责：【待HR确认】\n核心技能：【待HR确认】\n学历要求：【待HR确认】"
    assert _job_draft_is_weak(weak)


def test_detailed_job_profile_is_not_weak() -> None:
    detailed = """
    一、岗位定位：负责室内空间方案到施工落地的设计工作。
    二、主要职责：完成现场测量与需求访谈；输出平面布局与施工图；配合现场交底和变更闭环。
    三、核心专业技能：空间规划；材料与色彩搭配；节点深化及规范理解。
    四、工具与交付物：熟练使用AutoCAD；输出平面图、立面图和节点图；整理材料清单与效果表达。
    五、项目及行业经验：具备住宅或商业空间完整项目经验，能说明约束、方案和落地结果。
    六、学历与专业建议：室内设计、环境艺术设计、建筑装饰等相关专业优先。
    七、加分项：具备施工现场协调、成本意识及作品集表达能力。
    八、排除项或风险：无法独立完成规范施工图，或作品集无法说明本人职责和项目成果。
    九、面试核验要点：核验CAD图纸深度、现场问题处理、材料选型依据以及客户需求转化过程。
    """ * 2
    assert not _job_draft_is_weak(detailed)
