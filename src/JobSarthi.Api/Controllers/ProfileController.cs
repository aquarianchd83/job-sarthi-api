using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Resume;

namespace JobSarthi.Api.Controllers;

public record ProfileDto(string FullName, string Email, string Phone, string Headline, string Location,
    int TotalExperienceYears, List<string> Skills, string? ResumeFileName, DateTime? ResumeUploadedAtUtc);

[ApiController]
[Route("api/[controller]")]
public class ProfileController(JobSarthiDbContext db, IConfiguration config, IWebHostEnvironment env) : ControllerBase
{
    private const long MaxBytes = 5 * 1024 * 1024;

    [HttpGet]
    public async Task<ProfileDto> Get() => await BuildDto();

    [HttpPut]
    public async Task<IActionResult> Put(ProfileDto dto)
    {
        var p = await db.Profiles.FirstAsync();
        p.FullName = dto.FullName.Trim();
        p.Email = dto.Email.Trim();
        p.Phone = dto.Phone.Trim();
        p.Headline = dto.Headline.Trim();
        p.Location = dto.Location.Trim();
        p.TotalExperienceYears = Math.Clamp(dto.TotalExperienceYears, 0, 60);
        p.Skills = dto.Skills.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Upload PDF or DOCX. Parsed values replace the profile fields; edit them afterwards.</summary>
    [HttpPost("resume")]
    [RequestSizeLimit(MaxBytes)]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0) return BadRequest("Choose a file.");
        if (file.Length > MaxBytes) return BadRequest("File is larger than 5 MB.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".pdf" or ".docx")) return BadRequest("Only PDF and DOCX files are supported.");

        string text;
        try
        {
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            ms.Position = 0;
            text = ResumeParser.ExtractText(ms, ext);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BadRequest("Could not read this file. Is it a valid " + ext.TrimStart('.').ToUpper() + "?");
        }
        if (string.IsNullOrWhiteSpace(text)) return BadRequest("No text found. Scanned image resumes are not supported.");

        var dir = config["Storage:ResumeDir"] is { Length: > 0 } d ? d : Path.Combine(env.ContentRootPath, "data", "resumes");
        Directory.CreateDirectory(dir);
        var stored = Path.Combine(dir, Guid.NewGuid().ToString("N") + ext); // never use the client file name on disk
        await using (var fs = System.IO.File.Create(stored))
        {
            await file.OpenReadStream().CopyToAsync(fs);
        }

        db.Resumes.Add(new ResumeDocument
        {
            FileName = Path.GetFileName(file.FileName),
            StoragePath = stored,
            ParsedText = text,
            UploadedAtUtc = DateTime.UtcNow,
        });

        var parsed = ResumeParser.Parse(text);
        var p = await db.Profiles.FirstAsync();
        p.FullName = parsed.FullName;
        p.Email = parsed.Email;
        p.Phone = parsed.Phone;
        p.Headline = parsed.Headline;
        p.TotalExperienceYears = parsed.ExperienceYears;
        p.Skills = parsed.Skills;
        await db.SaveChangesAsync();
        return Ok(await BuildDto());
    }

    private async Task<ProfileDto> BuildDto()
    {
        var p = await db.Profiles.AsNoTracking().FirstAsync();
        var r = await db.Resumes.AsNoTracking().OrderByDescending(x => x.UploadedAtUtc).FirstOrDefaultAsync();
        return new ProfileDto(p.FullName, p.Email, p.Phone, p.Headline, p.Location, p.TotalExperienceYears,
            p.Skills, r?.FileName, r?.UploadedAtUtc);
    }
}
