using System.Text.Json;

namespace Pvm.Application.Documents;

/// <summary>A kind of paper the platform can read and file (design 6.3). Definitions live in code.</summary>
public interface IDocumentType
{
    string Code { get; }

    string DisplayName { get; }

    /// <summary>The Acumatica entity that gets the attachment.</summary>
    string AcumaticaEntity { get; }

    /// <summary>JSON Schema of the fields to extract, used as Claude's structured output.</summary>
    JsonElement FieldSchema { get; }

    /// <summary>The text for Claude that describes the papers and the fields.</summary>
    string ExtractionInstruction { get; }
}
