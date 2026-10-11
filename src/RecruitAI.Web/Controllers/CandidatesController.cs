using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RecruitAI.Application.Common;
using RecruitAI.Application.Recruitment;

namespace RecruitAI.Web.Controllers;

[ApiController]
[Authorize(Policy="Recruitment")]
public sealed class CandidatesController(IRecruitmentService service,IConfiguration configuration):ControllerBase
{
    [HttpGet("api/candidates")] public Task<CandidatePage> List(int page=1,int pageSize=10,string? keyword=null,string? status=null,CancellationToken ct=default)=>service.ListCandidatesAsync(page,pageSize,keyword,status,ct);
    [HttpGet("api/candidates/{id:long}")] public async Task<ActionResult<CandidateDetailDto>> Get(long id,CancellationToken ct){var item=await service.GetCandidateAsync(id,ct);return item is null?NotFound():item;}
    [HttpPut("api/candidates/{id:long}")]
    [HttpPost("api/candidates/{id:long}/update")] public async Task<IActionResult> Update(long id,UpdateCandidateRequest request,CancellationToken ct){await service.UpdateCandidateAsync(id,request,ct);return NoContent();}
    [HttpGet("api/resumes/{id:long}/download")]
    public async Task<IActionResult> Download(long id,CancellationToken ct)
    {
        var info=await service.GetResumeDownloadAsync(id,ct)??throw new BusinessException("RESUME_NOT_FOUND","简历不存在。",404);
        var root=Path.GetFullPath(configuration["FileStorage:ResumeRoot"]??@"C:\Zhaopin\Resume").TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(Path.IsPathRooted(info.RelativePath))throw new BusinessException("INVALID_RESUME_PATH","简历存储路径无效。",500);
        var full=Path.GetFullPath(Path.Combine(root,info.RelativePath));if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!System.IO.File.Exists(full)||ContainsReparsePoint(root,full))throw new BusinessException("RESUME_FILE_MISSING","简历文件不存在。",404);
        Response.Headers.CacheControl="private, no-store";Response.Headers.XContentTypeOptions="nosniff";
        var contentType = "application/json; charset=utf-8";
        return PhysicalFile(full,contentType,SanitizeDownloadName(info.FileName));
    }
    private static string SanitizeDownloadName(string name){var result=Path.GetFileName(name);foreach(var c in Path.GetInvalidFileNameChars())result=result.Replace(c,'_');return string.IsNullOrWhiteSpace(result)?"resume":result;}
    private static bool ContainsReparsePoint(string root,string full){var current=root.TrimEnd(Path.DirectorySeparatorChar);foreach(var part in Path.GetRelativePath(root,full).Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)){current=Path.Combine(current,part);if((Directory.Exists(current)||System.IO.File.Exists(current))&&System.IO.File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))return true;}return false;}
}
