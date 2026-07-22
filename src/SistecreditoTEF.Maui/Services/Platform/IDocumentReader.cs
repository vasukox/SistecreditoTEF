using System.Xml.Serialization;
using SistecreditoTEF.Maui.Models;

namespace SistecreditoTEF.Maui.Services.Platform;

/// <summary>
/// Lee el XML del documento de venta que HioPosCloud escribe en disco
/// cuando [OnlyUseDocumentPath=true] (doc §6).
///
/// POR QUE EXISTE (B7): sin esto, ventas grandes (>1MB) pierden lineas
/// porque HioPos trunca el Intent. Tambien extrae el SaleId, necesario
/// para idempotencia (B8).
/// </summary>
public interface IDocumentReader
{
    /// <summary>Lee el XML desde la ruta absoluta. Devuelve null si no existe.</summary>
    SaleDocument? Read(string documentPath);
}
