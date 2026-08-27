using System.Text;
using System.Xml;

namespace SIPackages.Tests;

internal sealed class PackageTests
{
    [Test]
    public void Clone_PreservesQualityControlSemanticState()
    {
        var package = new Package { Name = "Quality package", HasQualityControl = true };

        var clone = package.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone.HasQualityControl, Is.True);
            Assert.That(clone, Is.EqualTo(package));
            Assert.That(clone.GetHashCode(), Is.EqualTo(package.GetHashCode()));
        });

        clone.HasQualityControl = false;
        Assert.That(clone, Is.Not.EqualTo(package));
    }

    [Test]
    public void ContactUri_Serialize_Deserialize_Ok()
    {
        var package = new Package { ContactUri = "http://fakeuri" };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb))
        {
            package.WriteXml(writer);
        }

        var result = sb.ToString();

        var xmlDocument = new XmlDocument();
        xmlDocument.LoadXml(result);

        var contactUriValue = xmlDocument["package"]?.Attributes["contactUri"]?.Value;
        Assert.That(contactUriValue, Is.EqualTo("http://fakeuri"));

        var newPackage = new Package();

        using (var textReader = new StringReader(result))
        using (var reader = XmlReader.Create(textReader))
        {
            reader.ReadToDescendant("package");
            newPackage.ReadXml(reader);
        }

        Assert.That(newPackage.ContactUri, Is.EqualTo("http://fakeuri"));
    }
}
