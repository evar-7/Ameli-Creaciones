using System.Text.RegularExpressions;
using Ameli.Contracts;

namespace Ameli.Api.Application;

public sealed class CommerceOptions
{
    public decimal DiscountPercent { get; set; }
    public decimal TaxPercent { get; set; }
}

public sealed record PaymentAuthorization(bool Approved, string Provider, string Reference);

public interface IPaymentGateway
{
    Task<PaymentAuthorization> AuthorizeAsync(Guid ownerUserId, CheckoutRequest request, decimal amount, CancellationToken ct);
}

// Local demonstration adapter. Real payments require a provider's tokenized integration.
public sealed class DemoPaymentGateway(IHostEnvironment environment, TimeProvider clock) : IPaymentGateway
{
    public Task<PaymentAuthorization> AuthorizeAsync(Guid ownerUserId, CheckoutRequest request, decimal amount, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            throw new SecurityFault("payment_unavailable", "Configura una pasarela de pago para confirmar compras en este entorno.", 503);

        var errors = new Dictionary<string, string[]>();
        var number = (request.CardNumber ?? "").Replace(" ", "").Replace("-", "");
        if (number is not ("4242424242424242" or "5555555555554444" or "4000000000000002"))
            errors["CardNumber"] = ["Utiliza una de las tarjetas de prueba indicadas en el formulario."];
        if (string.IsNullOrWhiteSpace(request.CardholderName) || request.CardholderName.Trim().Length > 100)
            errors["CardholderName"] = ["Indica el nombre del titular."];
        if (!Regex.IsMatch(request.Cvc ?? "", "^[0-9]{3}$"))
            errors["Cvc"] = ["El CVC de prueba debe contener 3 dígitos."];
        var expiry = Regex.Match(request.ExpirationDate ?? "", "^(0[1-9]|1[0-2])/([0-9]{2}|[0-9]{4})$");
        var now = clock.GetUtcNow();
        var year = expiry.Success ? int.Parse(expiry.Groups[2].Value) : 0;
        if (year < 100) year += 2000;
        var month = expiry.Success ? int.Parse(expiry.Groups[1].Value) : 0;
        if (!expiry.Success || year < now.Year || year == now.Year && month < now.Month)
            errors["ExpirationDate"] = ["Indica una fecha de caducidad vigente con formato MM/AA."];
        if (errors.Count > 0)
            throw new SecurityFault("invalid_payment", "Revisa los datos de la tarjeta.") { Errors = errors };

        return Task.FromResult(new PaymentAuthorization(number != "4000000000000002", "Demo", "DEMO-" + ownerUserId.ToString("N")[..8] + "-" + request.RequestId.ToString("N")));
    }
}
