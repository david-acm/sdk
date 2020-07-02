﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the MIT license.  See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace Microsoft.CodeAnalysis.Tools.Formatters
{
    /// <summary>
    /// ImportsFormatter that uses the <see cref="Formatter"/> to format document import directives.
    /// </summary>
    internal sealed class ImportsFormatter : DocumentFormatter
    {
        protected override string FormatWarningDescription => Resources.Fix_imports_ordering;
        private readonly DocumentFormatter _endOfLineFormatter = new EndOfLineFormatter();

        internal override async Task<SourceText> FormatFileAsync(
            Document document,
            SourceText sourceText,
            OptionSet optionSet,
            AnalyzerConfigOptions analyzerConfigOptions,
            FormatOptions formatOptions,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            try
            {
                // Only run formatter if the user has specifically configured one of the driving properties.
                if (!analyzerConfigOptions.TryGetValue("dotnet_sort_system_directives_first", out _) &&
                    !analyzerConfigOptions.TryGetValue("dotnet_separate_import_directive_groups", out _))
                {
                    return sourceText;
                }

                var organizedDocument = await Formatter.OrganizeImportsAsync(document, cancellationToken);

                var isSameVersion = await IsSameDocumentAndVersionAsync(document, organizedDocument, cancellationToken).ConfigureAwait(false);
                if (isSameVersion)
                {
                    return sourceText;
                }

                // Because the Formatter does not abide the `end_of_line` option we have to fix up the ends of the organized lines.
                // See https://github.com/dotnet/roslyn/issues/44136
                var organizedSourceText = await organizedDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
                return await _endOfLineFormatter.FormatFileAsync(organizedDocument, organizedSourceText, optionSet, analyzerConfigOptions, formatOptions, logger, cancellationToken).ConfigureAwait(false);
            }
            catch (InsufficientExecutionStackException)
            {
                // This case is normally not hit when running against a handwritten code file.
                // https://github.com/dotnet/roslyn/issues/44710#issuecomment-636253053
                logger.LogWarning(Resources.Unable_to_organize_imports_for_0_The_document_is_too_complex, Path.GetFileName(document.FilePath));
                return sourceText;
            }
        }

        private static async Task<bool> IsSameDocumentAndVersionAsync(Document a, Document b, CancellationToken cancellationToken)
        {
            if (a == b)
            {
                return true;
            }

            if (a.Id != b.Id)
            {
                return false;
            }

            var aVersion = await a.GetTextVersionAsync(cancellationToken).ConfigureAwait(false);
            var bVersion = await b.GetTextVersionAsync(cancellationToken).ConfigureAwait(false);

            return aVersion == bVersion;
        }
    }
}

