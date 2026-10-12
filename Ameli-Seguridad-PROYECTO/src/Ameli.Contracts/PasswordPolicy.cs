namespace Ameli.Contracts;

public static class PasswordPolicy
{
    public static string[] UnmetRequirements(string? password)
    {
        var value = password ?? "";
        List<string> errors = [];
        if (value.Length is < 8 or > 64) errors.Add("La contraseña debe tener entre 8 y 64 caracteres.");
        if (!value.Any(char.IsUpper)) errors.Add("Agrega al menos una letra mayúscula.");
        if (!value.Any(char.IsLower)) errors.Add("Agrega al menos una letra minúscula.");
        if (!value.Any(char.IsDigit)) errors.Add("Agrega al menos un número.");
        if (!value.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)))
            errors.Add("Agrega al menos un carácter especial (símbolo).");
        return [.. errors];
    }
}
