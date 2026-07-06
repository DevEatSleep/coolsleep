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

    public static string ToPromptDescription(this HousingType h) => h switch
    {
        HousingType.Climatise       => "un logement climatisé",
        HousingType.MaisonRdc       => "une maison de plain-pied",
        HousingType.MaisonEtage     => "une maison à étage",
        HousingType.MaisonSousToits => "une maison avec des pièces sous les toits",
        HousingType.AppartBas       => "un appartement en étage bas",
        HousingType.AppartHaut      => "un appartement en étage haut",
        HousingType.SousToits       => "un logement sous les toits",
        _                           => h.ToString()
    };
}
