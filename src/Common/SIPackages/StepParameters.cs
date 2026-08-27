using SIPackages.Helpers;
using SIPackages.Models;
using System.Xml;

namespace SIPackages;

/// <summary>
/// Defines a collection of step parameters.
/// </summary>
public sealed class StepParameters : Dictionary<string, StepParameter>, IEquatable<StepParameters>
{
    /// <inheritdoc />
    public bool Equals(StepParameters? other) => other is not null && this.SequenceEqual(other);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StepParameters);

    /// <inheritdoc />
    public override int GetHashCode() => this.GetCollectionHashCode();

    /// <inheritdoc />
    public void ReadXml(XmlReader reader, PackageLimits? limits)
    {
        var parentTagName = reader.LocalName;
        var parentDepth = reader.Depth;

        if (reader.IsEmptyElement)
        {
            reader.Read();
            return;
        }

        if (!reader.Read())
        {
            return;
        }

        while (reader.ReadState == ReadState.Interactive)
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == parentDepth
                && reader.LocalName == parentTagName)
            {
                reader.Read();
                return;
            }

            if (reader.NodeType == XmlNodeType.Element
                && reader.Depth == parentDepth + 1
                && reader.LocalName == "param")
            {
                if (!reader.IsEmptyElement && (limits == null || Count < limits.ParameterCount))
                {
                    var name = reader.GetAttribute("name")?.LimitLengthBy(limits?.TextLength) ?? "";
                    var parameter = new StepParameter();

                    using (var parameterReader = reader.ReadSubtree())
                    {
                        parameterReader.Read();
                        parameter.ReadXml(parameterReader, limits);
                    }

                    this[name] = parameter;
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
        foreach (var item in this)
        {
            writer.WriteStartElement("param");
            writer.WriteAttributeString("name", item.Key);
            item.Value.WriteXml(writer);
            writer.WriteEndElement();
        }
    }

    internal StepParameters Clone()
    {
        var result = new StepParameters();

        foreach (var parameter in this)
        {
            result[parameter.Key] = parameter.Value.Clone();
        }

        return result;
    }
}
