using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;

namespace HubitatVS
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("code")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class HubitatAdornmentProvider : IWpfTextViewCreationListener
    {
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("HubitatAdornment")]
        [Order(After = PredefinedAdornmentLayers.Selection, Before = PredefinedAdornmentLayers.Squiggle)]
#pragma warning disable 649
        internal AdornmentLayerDefinition _adornmentLayerDefinition;
#pragma warning restore 649

        [Import]
        internal ITextDocumentFactoryService TextDocumentFactory { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            if (!TextDocumentFactory.TryGetTextDocument(textView.TextBuffer, out var doc))
                return;

            if (!doc.FilePath.EndsWith(".groovy", StringComparison.OrdinalIgnoreCase))
                return;

            _ = new HubitatAdornment(textView, doc);
        }
    }
}
