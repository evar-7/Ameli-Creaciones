using System.Buffers.Binary;
using Ameli.Contracts;

namespace Ameli.Api.Application;

public static class ImageValidation
{
    private static string? PhotoType(byte[] bytes)
    {
        if (bytes.Length >= 20 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF &&
            bytes[^2] == 0xFF && bytes[^1] == 0xD9) return "image/jpeg";
        if (bytes.Length >= 45 && bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) && bytes.AsSpan(bytes.Length - 8, 4).SequenceEqual("IEND"u8))
            return "image/png";
        if (bytes.Length >= 20 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
            bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8) &&
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) + 8L == bytes.Length &&
            (bytes.AsSpan(12, 4).SequenceEqual("VP8 "u8) || bytes.AsSpan(12, 4).SequenceEqual("VP8L"u8) ||
             bytes.AsSpan(12, 4).SequenceEqual("VP8X"u8))) return "image/webp";
        return null;
    }

    public static (byte[] bytes, string contentType) Validate(PetPhotoRequest request)
    {
        const int maxBytes = 5 * 1024 * 1024;
        if (request.Base64Data?.Length > ((maxBytes + 2) / 3) * 4 + 8)
            throw new SecurityFault("large_image", "No se admiten imagenes superiores a 5 MB")
            { Errors = new() { ["Photo"] = ["No se admiten imagenes superiores a 5 MB"] } };
        byte[] bytes;
        try { bytes = Convert.FromBase64String(request.Base64Data ?? ""); }
        catch (FormatException) { bytes = []; }
        if (bytes.Length > maxBytes)
            throw new SecurityFault("large_image", "No se admiten imagenes superiores a 5 MB")
            { Errors = new() { ["Photo"] = ["No se admiten imagenes superiores a 5 MB"] } };
        var kind = PhotoType(bytes);
        var ext = Path.GetExtension(request.FileName ?? "").ToLowerInvariant();
        if (kind is null || !((kind == "image/jpeg" && ext is ".jpg" or ".jpeg") ||
            (kind == "image/png" && ext == ".png") || (kind == "image/webp" && ext == ".webp")))
            throw new SecurityFault("invalid_image", "Formato de imagen no valida solo se adminten imagenes en formato JPG, PNG o WEBP")
            { Errors = new() { ["Photo"] = ["Formato de imagen no valida solo se adminten imagenes en formato JPG, PNG o WEBP"] } };
        return (bytes, kind);
    }

}
