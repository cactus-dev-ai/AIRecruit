using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RecruitAI.Web.Controllers;

public sealed class HomeController : Controller
{
    [HttpGet("/")]
    public IActionResult Index() => View();

    [HttpGet("/imports")]
    public IActionResult Imports() => View();

    [HttpGet("/imports/{id:long}")]
    public IActionResult ImportDetail(long id) => View(model: id);

    [HttpGet("/knowledge")]
    public IActionResult Knowledge() => View();

    [HttpGet("/knowledge/imports/{id:long}")]
    public IActionResult KnowledgeImport(long id) => View(model: id);

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "ADMIN")]
    [HttpGet("/admin/users")]
    public IActionResult Users() => View();

    [HttpGet("/jobs")]
    public IActionResult Jobs() => View();

    [HttpGet("/jobs/{id:long}")]
    public IActionResult JobDetail(long id) => View(model:id);

    [HttpGet("/candidates")]
    public IActionResult Candidates() => View();

    [HttpGet("/candidates/{id:long}")]
    public IActionResult CandidateDetail(long id) => View(model:id);

    [HttpGet("/tasks")]
    public IActionResult Tasks() => View();

    [HttpGet("/scores")]
    public IActionResult Scores() => View();

    [HttpGet("/favorites")]
    public IActionResult Favorites() => View();

    [HttpGet("/interviews")]
    public IActionResult Interviews() => View();

    [HttpGet("/interviews/plans")]
    public IActionResult InterviewPlans() => View();

    [HttpGet("/interviews/question-sets/{id:long}")]
    public IActionResult InterviewQuestionSet(long id) => View(model:id);

    [HttpGet("/interviews/{id:long}")]
    public IActionResult InterviewDetail(long id) => View(model:id);

    [AllowAnonymous]
    [HttpGet("/error")]
    public IActionResult Error() => Content("系统发生错误，请联系管理员。", "text/plain; charset=utf-8");
}
