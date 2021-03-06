﻿using System;
using System.ComponentModel.Composition;
using EnvDTE;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using TechTalk.SpecFlow.Generator.Configuration;
using TechTalk.SpecFlow.Vs2010Integration.Options;
using TechTalk.SpecFlow.Vs2010Integration.Tracing;

namespace TechTalk.SpecFlow.Vs2010Integration
{
    [Export(typeof(ISpecFlowServices))]
    internal class SpecFlowServices : ISpecFlowServices
    {
        [Import]
        internal SVsServiceProvider ServiceProvider = null;

        [Import]
        internal IVsEditorAdaptersFactoryService AdaptersFactory = null;

        [Import]
        internal IVisualStudioTracer VisualStudioTracer = null;

        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry = null; // Set via MEF

        public IntegrationOptions GetOptions()
        {
            var dte = VsxHelper.GetDte(ServiceProvider);
            return IntegrationOptionsProvider.GetOptions(dte);
        }

        public Project GetProject(ITextBuffer textBuffer)
        {
            return VsxHelper.GetCurrentProject(textBuffer, AdaptersFactory, ServiceProvider);
        }

        public static bool IsProjectSupported(Project project)
        {
            return
                project.FullName.EndsWith(".csproj") ||
                project.FullName.EndsWith(".vbproj");
        }
    }
}
