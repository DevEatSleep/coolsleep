namespace CoolSleep.Api.Core;

public static class HousingTypeExtensions
{
    public static string ToSnakeCase(this HousingType h) => h switch
    {
        HousingType.Climatise       => "climatise",
        HousingType.MaisonRdc       => "maison_rdc",
        HousingType.MaisonEtage     => "maison_etage",
        HousingType.MaisonSousToits => "maison_sous_toits",
        HousingType.AppartBas       => "appart_bas",
        HousingType.AppartHaut      => "appart_haut",
        HousingType.SousToits       => "sous_toits",
        _                           => h.ToString().ToLowerInvariant()
    };
}
