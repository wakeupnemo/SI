using SIPackages.Helpers;
using SIPackages.Models;
using System.Xml;

namespace SIPackages;

/// <summary>
/// Defines a question script as a sequence of steps.
/// </summary>
public sealed class Script : IEquatable<Script>
{
    /// <summary>
    /// Script steps which are executed sequentially.
    /// </summary>
    public List<Step> Steps { get; } = new();

    /// <inheritdoc />
    public override string ToString() => string.Join(" => ", Steps);

    /// <inheritdoc />
    public bool Equals(Script? other) => other is not null && Steps.SequenceEqual(other.Steps);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Script);

    /// <inheritdoc />
    public override int GetHashCode() => Steps.GetCollectionHashCode();

    internal Script Clone()
    {
        var script = new Script();
        script.Steps.AddRange(Steps.Select(step => step.Clone()));
        return script;
    }

    /// <inheritdoc />
    public void ReadXml(XmlReader reader, PackageLimits? limits)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        var scriptDepth = reader.Depth;

        if (!reader.Read())
        {
            return;
        }

        while (reader.ReadState == ReadState.Interactive)
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == scriptDepth
                && reader.LocalName == "script")
            {
                reader.Read();
                return;
            }

            if (reader.NodeType == XmlNodeType.Element
                && reader.Depth == scriptDepth + 1
                && reader.LocalName == "step")
            {
                if (limits == null || Steps.Count < limits.StepCount)
                {
                    var step = new Step();

                    using (var stepReader = reader.ReadSubtree())
                    {
                        stepReader.Read();
                        step.ReadXml(stepReader, limits);
                    }

                    Steps.Add(step);
                    reader.Read();
                }
                else
                {
                    reader.Skip();
                }

                continue;
            }

            reader.Read();
        }
    }

    /// <inheritdoc />
    public void WriteXml(XmlWriter writer)
    {
        foreach (var step in Steps)
        {
            writer.WriteStartElement("step");
            step.WriteXml(writer);
            writer.WriteEndElement();
        }
    }
}
