namespace Calorias.Application.Servicios;

/// <summary>Diccionario curado EN→ES de alimentos comunes (clave case-insensitive).</summary>
public static class DiccionarioAlimentos
{
    public static readonly IReadOnlyDictionary<string, string> EsPorIngles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["apple"] = "Manzana",
            ["banana"] = "Plátano",
            ["orange"] = "Naranja",
            ["rice"] = "Arroz",
            ["white rice"] = "Arroz blanco",
            ["chicken"] = "Pollo",
            ["chicken breast"] = "Pechuga de pollo",
            ["beef"] = "Carne de res",
            ["pork"] = "Cerdo",
            ["fish"] = "Pescado",
            ["egg"] = "Huevo",
            ["eggs"] = "Huevos",
            ["bread"] = "Pan",
            ["milk"] = "Leche",
            ["cheese"] = "Queso",
            ["potato"] = "Papa",
            ["tomato"] = "Tomate",
            ["lettuce"] = "Lechuga",
            ["carrot"] = "Zanahoria",
            ["onion"] = "Cebolla",
            ["beans"] = "Frijoles",
            ["pasta"] = "Pasta",
            ["pizza"] = "Pizza",
            ["salad"] = "Ensalada",
            ["soup"] = "Sopa",
            ["yogurt"] = "Yogur",
            ["butter"] = "Mantequilla",
            ["avocado"] = "Aguacate",
            ["corn"] = "Maíz",
            ["strawberry"] = "Fresa",
            ["grapes"] = "Uvas",
            ["salmon"] = "Salmón",
            ["tuna"] = "Atún",
            ["shrimp"] = "Camarón",
            ["broccoli"] = "Brócoli",
            ["spinach"] = "Espinaca",
            ["mushroom"] = "Champiñón",
            ["coffee"] = "Café",
            ["water"] = "Agua",
            ["juice"] = "Jugo",
        };
}
