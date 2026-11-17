using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSarthi.Api.Data;
using JobSarthi.Api.Domain.Entities;

namespace JobSarthi.Api.Controllers;

public record SavedAnswerDto(int Id, string Question, string Answer);
public record SavedAnswerRequest(string Question, string Answer);

[ApiController]
[Route("api/saved-answers")]
public class SavedAnswersController(JobSarthiDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<SavedAnswerDto>> List() =>
        await db.SavedAnswers.AsNoTracking().OrderBy(a => a.Question).Select(a => new SavedAnswerDto(a.Id, a.Question, a.Answer)).ToListAsync();

    [HttpPost]
    public async Task<ActionResult<SavedAnswerDto>> Add(SavedAnswerRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Question) || string.IsNullOrWhiteSpace(req.Answer)) return BadRequest("Question and answer are both required.");
        var q = req.Question.Trim();
        if (await db.SavedAnswers.AnyAsync(a => a.Question == q)) return Conflict("You already saved an answer for that question. Edit it instead.");
        var row = new SavedAnswer { Question = q, Answer = req.Answer.Trim() };
        db.SavedAnswers.Add(row);
        await db.SaveChangesAsync();
        return new SavedAnswerDto(row.Id, row.Question, row.Answer);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SavedAnswerRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Question) || string.IsNullOrWhiteSpace(req.Answer)) return BadRequest("Question and answer are both required.");
        var row = await db.SavedAnswers.FindAsync(id);
        if (row is null) return NotFound();
        row.Question = req.Question.Trim();
        row.Answer = req.Answer.Trim();
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await db.SavedAnswers.FindAsync(id);
        if (row is null) return NotFound();
        db.SavedAnswers.Remove(row);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
