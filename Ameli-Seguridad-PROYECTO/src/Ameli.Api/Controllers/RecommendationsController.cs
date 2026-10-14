using System.Security.Claims;
using Ameli.Api.Application;
using Ameli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ameli.Api.Controllers;

[ApiController, Route("api/v1/customer/recommendations"), Authorize(Roles = Roles.Client)]
public sealed class RecommendationsController(RecommendationService service) : ControllerBase
{
    private Actor Actor => new(Guid.Parse(User.FindFirstValue("sub")!), Guid.Parse(User.FindFirstValue("sid")!));

    private async Task<ActionResult<T>> Respond<T>(Func<Task<T>> operation)
    {
        try { return Ok(await operation()); }
        catch (SecurityFault fault) { return StatusCode(fault.Status, fault.ToError()); }
    }

    [HttpGet]
    public Task<ActionResult<RecommendationResultView>> List([FromQuery] RecommendationQuery query, CancellationToken ct) =>
        Respond(() => service.RecommendationsAsync(Actor, query, ct));

    [HttpGet("explanation")]
    public Task<ActionResult<RecommendationExplanationView>> Explanation([FromQuery] RecommendationExplanationQuery query, CancellationToken ct) =>
        Respond(() => service.ExplanationAsync(Actor, query, ct));

    [HttpPost("feedback")]
    public Task<ActionResult<RecommendationFeedbackView>> Feedback(RecommendationFeedbackRequest request, CancellationToken ct) =>
        Respond(() => service.SaveFeedbackAsync(Actor, request, ct));
}
