using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Api.Application.Messages;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Presentation;

namespace PaymentGateway.Api.Controllers;

/// <summary>Manages payment transactions.</summary>
[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class PaymentsController : ControllerBase
{
    private readonly IRequestClient<PostPaymentCommand> _postPaymentClient;
    private readonly IRequestClient<GetPaymentQuery>    _getPaymentClient;
    private readonly string                             _idempotencyHeader;
    private readonly ILogger<PaymentsController>        _logger;

    public PaymentsController(
        IRequestClient<PostPaymentCommand> postPaymentClient,
        IRequestClient<GetPaymentQuery>    getPaymentClient,
        IOptions<IdempotencyOptions>       idempotencyOptions,
        ILogger<PaymentsController>        logger)
    {
        _postPaymentClient = postPaymentClient;
        _getPaymentClient  = getPaymentClient;
        _idempotencyHeader = idempotencyOptions.Value.HeaderName;
        _logger            = logger;
    }

    /// <summary>Submit a new payment for processing.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaymentResponse>> PostPaymentAsync([FromBody] PostPaymentRequest request)
    {
        if (!Request.Headers.TryGetValue(_idempotencyHeader, out var idempotencyHeader) ||
            string.IsNullOrWhiteSpace(idempotencyHeader.FirstOrDefault()))
        {
            _logger.LogWarning("POST /api/payments rejected: missing or blank {Header}", _idempotencyHeader);
            return BadRequest($"{_idempotencyHeader} header is required.");
        }

        if (!ModelState.IsValid)
        {
            _logger.LogWarning("POST /api/payments rejected: invalid request body");
            return BadRequest(ModelState);
        }

        var command = new PostPaymentCommand(
            idempotencyHeader.First()!,
            request.CardNumber,
            request.ExpiryMonth,
            request.ExpiryYear,
            request.Currency,
            request.Amount,
            request.Cvv);

        var response = await _postPaymentClient.GetResponse<PaymentResponse>(command);
        return Ok(response.Message);
    }

    /// <summary>Retrieve a previously processed payment by its ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentResponse>> GetPayment(Guid id)
    {
        var response = await _getPaymentClient.GetResponse<PaymentResponse, PaymentNotFound>(new GetPaymentQuery(id));
        if (response.Is(out Response<PaymentResponse>? found))
            return Ok(found.Message);

        _logger.LogWarning("GET /api/payments/{PaymentId} — not found", id);
        return NotFound();
    }
}
