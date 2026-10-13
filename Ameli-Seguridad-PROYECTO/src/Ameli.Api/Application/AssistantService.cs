using System.Text.RegularExpressions;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Application;

public sealed class AssistantService(SecurityDbContext db, ProductInformationService products,
    CommerceService commerce, IProfileRecommendationEngine recommendations)
{
    private static ChatReply Reply(ChatRequest request, string kind, string message, List<ChatProductCard>? cards = null,
        List<MissingChatField>? missing = null, SizeAdvice? size = null, OrderView? order = null, string? url = null, string? label = null) =>
        new(request.RequestId, kind, message, cards ?? [], missing ?? [], size, order, url, label);
    private static ChatReply Login(ChatRequest request) => Reply(request, "login_required",
        "Inicia sesión con tu cuenta de cliente para consultar pedidos o perfiles. Un correo, teléfono u otros datos personales no sustituyen la autenticación.",
        url: "/ingresar", label: "Iniciar sesión");

    public async Task<ChatReply> RespondAsync(Actor? actor, ChatRequest request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty) throw new SecurityFault("invalid_request", "La solicitud necesita un identificador.");
        var message = request.Message?.Trim() ?? "";
        var text = ProductSizing.Fold(message);
        var intent = request.Intent;
        if (intent == "auto")
        {
            if (Regex.IsMatch(text, @"\b(pedido|pedidos|transportista|seguimiento|envio)\b")) intent = "order";
            else if (Regex.IsMatch(text, @"\b(talla|tallas|medidas|guia visual)\b")) intent = "size";
            else if (Regex.IsMatch(text, @"\b(recomienda|recomiendame|recomendacion|recomendaciones|sugerencia|sugerencias)\b")) intent = "recommendations";
            else if (request.ProductId.HasValue || Regex.IsMatch(text, @"\b(producto|productos|catalogo|precio|stock|material|materiales|cuidados|agotado|disponibilidad)\b")) intent = "catalog";
        }
        if (intent is "order" or "recommendations")
        {
            if (actor is null) return Login(request);
            if (!await db.Users.AnyAsync(u => u.Id == actor.UserId && u.IsActive && u.RoleName == Roles.Client, ct))
                return Reply(request, "client_required", "Esta función está disponible para las cuentas de cliente.");
        }
        if (intent == "order")
        {
            var number = request.OrderNumber?.Trim() ?? "";
            if (number.Length == 0) number = Regex.Match(message, @"\bAML-[a-fA-F0-9]{32}\b", RegexOptions.IgnoreCase).Value;
            if (number.Length == 0) number = Regex.Match(message, @"\bpedido\s+(?:n[uú]mero\s*|#\s*)?([A-Za-z0-9][A-Za-z0-9_-]{1,79})", RegexOptions.IgnoreCase).Groups[1].Value;
            if (number.Length == 0) return Reply(request, "missing_order", "Indica el número que aparece en Mis pedidos.",
                missing: [new("OrderNumber", "Número de pedido")], url: "/mis-pedidos", label: "Mis pedidos");
            var order = await commerce.OwnOrderAsync(actor!, number, ct);
            if (order is null) return Reply(request, "order_unavailable", "No puedo consultar ese pedido. Verifica el número desde Mis pedidos.", url: "/mis-pedidos", label: "Mis pedidos");
            // Only tracking information is included in the chat response.
            var tracking = order with { DeliverySnapshot = "", Items = [], Total = 0 };
            return Reply(request, "order", $"El pedido {order.Number} está {order.Status}.", order: tracking, url: "/mis-pedidos", label: "Ver Mis pedidos");
        }
        if (intent == "recommendations")
        {
            if (!request.PetId.HasValue) return Reply(request, "missing_pet", "Selecciona la mascota para la que quieres sugerencias.",
                missing: [new("PetId", "Mascota")], url: "/perfil/mascotas", label: "Mis mascotas");
            var pet = await db.Pets.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PetId && p.OwnerUserId == actor!.UserId, ct);
            if (pet is null) return Reply(request, "pet_unavailable", "No puedo consultar ese perfil. Revisa Mis mascotas.", url: "/perfil/mascotas", label: "Mis mascotas");
            var missing = new List<MissingChatField>();
            if (!pet.IsActive) missing.Add(new("IsActive", "Reactivar perfil"));
            if (string.IsNullOrWhiteSpace(pet.Species)) missing.Add(new("Species", "Especie"));
            if (string.IsNullOrWhiteSpace(pet.Breed)) missing.Add(new("Breed", "Raza"));
            if (pet.HeightCm <= 0) missing.Add(new("HeightCm", "Talla / altura (cm)"));
            if (pet.WeightKg <= 0) missing.Add(new("WeightKg", "Peso (kg)"));
            if (!pet.AllergiesDeclared) missing.Add(new("AllergiesDeclared", "Declarar alergias o confirmar que no tiene alergias conocidas"));
            if (missing.Count > 0) return Reply(request, "incomplete_pet", "Completa estos datos antes de recibir recomendaciones:", missing: missing,
                url: $"/perfil/mascotas/{pet.Id}", label: "Editar perfil de " + pet.Name);
            var cards = recommendations.Recommend(pet, await products.RecommendationCandidatesAsync(pet.Species, ct));
            return Reply(request, "recommendations", cards.Count > 0 ? $"Estas opciones están disponibles para {pet.Name}:"
                : "No encontré productos con stock, material declarado y una talla compatible de forma segura. Puedes consultar el catálogo o actualizar el perfil.", cards,
                url: "/productos", label: "Ver catálogo");
        }
        if (intent == "size")
        {
            if (!request.ProductId.HasValue)
            { var named = await products.IdentifyAsync(message, ct); if (named.Count == 1) request.ProductId = named[0].Id; }
            if (string.IsNullOrWhiteSpace(request.HeightCm)) request.HeightCm = Regex.Match(text, @"(?<![\p{L}\p{N}.,])(-?[0-9]+(?:[.,][0-9]+)?)\s*cm\b").Groups[1].Value.Replace(',', '.');
            if (string.IsNullOrWhiteSpace(request.WeightKg)) request.WeightKg = Regex.Match(text, @"(?<![\p{L}\p{N}.,])(-?[0-9]+(?:[.,][0-9]+)?)\s*kg\b").Groups[1].Value.Replace(',', '.');
            if (string.IsNullOrWhiteSpace(request.Species))
            { if (Regex.IsMatch(text, @"\bperro\b")) request.Species = ProductSpecies.Dog; else if (Regex.IsMatch(text, @"\bgato\b")) request.Species = ProductSpecies.Cat; }
            return await SizeAsync(request, ct);
        }
        var matches = request.ProductId.HasValue ? new List<StoreProductView> { await products.ProductAsync(request.ProductId.Value, ct) }
            : await products.IdentifyAsync(message, ct);
        if (intent == "catalog" || (intent == "auto" && matches.Count > 0 && message.Length > 0 &&
            matches.Any(p => text.Contains(ProductSizing.Fold(p.Name), StringComparison.Ordinal) || text.Contains(ProductSizing.Fold(p.Sku), StringComparison.Ordinal))))
        {
            if (matches.Count == 0) return Reply(request, "catalog_empty", "No encontré un producto activo con ese nombre o SKU. Verifica el nombre o consulta el catálogo.", url: "/productos", label: "Ver catálogo");
            var cards = matches.Take(8).Select(p => new ChatProductCard(p, null, p.StockQuantity > 0 ? "Disponible al momento de consultar." : "Agotado.", null)).ToList();
            if (matches.Count == 1 && matches[0].StockQuantity == 0)
                cards.AddRange((await products.AlternativesAsync(matches[0], ct)).Select(p => new ChatProductCard(p, null, "Alternativa de la misma categoría con stock.", null)));
            return Reply(request, "catalog", matches.Count == 1 && matches[0].StockQuantity == 0 ? "Agotado. Estas son las alternativas disponibles de la misma categoría:"
                : "Estos son los datos actuales del catálogo:", cards);
        }
        return Reply(request, "out_of_scope", "Puedo ayudarte con productos, materiales, disponibilidad, guías de tallas, recomendaciones para tus mascotas y el estado de tus pedidos. Elige una función para continuar.");
    }
    private async Task<ChatReply> SizeAsync(ChatRequest request, CancellationToken ct)
    {
        var missing = new List<MissingChatField>();
        if (!request.ProductId.HasValue) missing.Add(new("ProductId", "Producto (nombre o SKU)"));
        if (string.IsNullOrWhiteSpace(request.Species)) missing.Add(new("Species", "Especie"));
        if (string.IsNullOrWhiteSpace(request.Breed)) missing.Add(new("Breed", "Raza"));
        if (!ProductSizing.Positive(request.HeightCm, out var height) || height > 250) missing.Add(new("HeightCm", "Talla / altura positiva en cm (hasta 250 cm)"));
        if (!ProductSizing.Positive(request.WeightKg, out var weight) || weight > 200) missing.Add(new("WeightKg", "Peso positivo en kg (hasta 200 kg)"));
        if (missing.Count > 0) return Reply(request, "missing_measures", "Faltan datos o las medidas no son válidas. Completa exactamente estos campos; no calcularé una talla con información incompleta:", missing: missing);
        var product = await products.ProductAsync(request.ProductId!.Value, ct);
        var advice = ProductSizing.Evaluate(product, request.Species.Trim(), height, weight);
        return Reply(request, "size", advice.Message, [new(product, advice.SuggestedSize, "Tabla de tallas de este producto.", null)], size: advice,
            url: product.GuideUrl, label: "Guía visual del producto");
    }
}
