using Calorias.Application.Abstractions;
using Calorias.Domain.Entities;

namespace Calorias.Application.Servicios;

/// <summary>
/// Construye un RegistroComida (1 detalle) a partir de una EtiquetaNutricional escalada
/// por el número de porciones consumidas. Puro: sin BD ni dependencias.
/// </summary>
public static class RegistroDesdeEtiqueta
{
    public static RegistroComida Construir(
        EtiquetaNutricional etq, decimal porciones, string usuarioId,
        TipoComida tipo, DateOnly fechaLocal)
    {
        var detalle = new DetalleComida
        {
            NombreAlimento = string.IsNullOrWhiteSpace(etq.NombreProducto) ? "Producto" : etq.NombreProducto!,
            Cantidad = etq.TamPorcion * porciones,
            UnidadMedida = etq.UnidadPorcion,
            Calorias = etq.CaloriasPorPorcion * porciones,
            Proteinas = etq.ProteinaPorPorcion * porciones,
            Carbohidratos = etq.CarbosPorPorcion * porciones,
            Grasas = etq.GrasasPorPorcion * porciones,
            ConfianzaDeteccion = 1d,
        };

        var registro = new RegistroComida
        {
            UsuarioId = usuarioId,
            Tipo = tipo,
            FechaLocal = fechaLocal,
            Detalles = new List<DetalleComida> { detalle },
            CaloriasTotales = detalle.Calorias,
            ProteinasTotales = detalle.Proteinas,
            CarbohidratosTotales = detalle.Carbohidratos,
            GrasasTotales = detalle.Grasas,
        };
        return registro;
    }
}
