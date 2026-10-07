namespace Content.Shared._radiant.Dossiers;

/// <summary>Persistent, non-editable blood group for a character profile.</summary>
public static class DossierBloodGroup
{
    public static string Roll(int value) => value switch
    {
        < 37 => "O+",
        < 43 => "O-",
        < 77 => "A+",
        < 83 => "A-",
        < 93 => "B+",
        < 95 => "B-",
        < 99 => "AB+",
        _ => "AB-",
    };

    public static bool IsValid(string? value) => value is "O+" or "O-" or "A+" or "A-" or "B+" or "B-" or "AB+" or "AB-";
}
