using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Furdeco_ChatBot.Controllers
{
    [Route("api/canned-replies")]
    [ApiController]
    //[Authorize]
    public class CannedReplyController : ControllerBase
    {
        private readonly CannedReplyService _replies;

        public CannedReplyController(CannedReplyService replies) => _replies = replies;

        // GET /api/canned-replies
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await _replies.GetAllAsync();
            return Ok(list.Select(c => new { c.Id, c.Text, c.SortOrder }));
        }

        // POST /api/canned-replies
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CannedReplyRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Text))
                return BadRequest(new { error = "Reply text is required." });

            var c = await _replies.CreateAsync(req.Text);
            return Ok(new { c.Id, c.Text, c.SortOrder });
        }

        // PUT /api/canned-replies/{id}
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] CannedReplyRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Text))
                return BadRequest(new { error = "Reply text is required." });

            var c = await _replies.UpdateAsync(id, req.Text);
            if (c == null) return NotFound();
            return Ok(new { c.Id, c.Text, c.SortOrder });
        }

        // DELETE /api/canned-replies/{id}
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var ok = await _replies.DeleteAsync(id);
            if (!ok) return NotFound();
            return Ok();
        }

        public record CannedReplyRequest(string Text);
    }
}
