using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace MutedBoilerplate.VS.Classification;

/// <summary>
/// MEF-exports the classification types used by the muting classifier. The three
/// built-ins are first-class; eight extra "user slots" let user-defined categories
/// bind to a stable classification type without dynamic MEF tricks.
/// </summary>
internal static class MutedClassificationTypes
{
#pragma warning disable CS0649 // Field is never assigned to (MEF imports the values).
#pragma warning disable IDE0044, CS8618

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Constants.ClassTelemetry)]
    internal static ClassificationTypeDefinition Telemetry;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Constants.ClassLogging)]
    internal static ClassificationTypeDefinition Logging;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Constants.ClassSignature)]
    internal static ClassificationTypeDefinition Signature;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name(Constants.ClassGuards)]
    internal static ClassificationTypeDefinition Guards;

    [Export(typeof(ClassificationTypeDefinition))]
    [Name("muted.user1")] internal static ClassificationTypeDefinition User1;
    [Export(typeof(ClassificationTypeDefinition))]
    [Name("muted.user2")] internal static ClassificationTypeDefinition User2;
    [Export(typeof(ClassificationTypeDefinition))]
    [Name("muted.user3")] internal static ClassificationTypeDefinition User3;
    [Export(typeof(ClassificationTypeDefinition))]
    [Name("muted.user4")] internal static ClassificationTypeDefinition User4;
    [Export(typeof(ClassificationTypeDefinition))]
    [Name("muted.user5")] internal static ClassificationTypeDefinition User5;
    [Export(typeof(ClassificationTypeDefinition))]
    [Name("muted.user6")] internal static ClassificationTypeDefinition User6;
    [Export(typeof(ClassificationTypeDefinition))]
    [Name("muted.user7")] internal static ClassificationTypeDefinition User7;
    [Export(typeof(ClassificationTypeDefinition))]
    [Name("muted.user8")] internal static ClassificationTypeDefinition User8;

#pragma warning restore IDE0044, CS8618
#pragma warning restore CS0649
}
