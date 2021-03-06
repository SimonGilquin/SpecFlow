﻿using System;
using System.Collections.Generic;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Utilities;
using TechTalk.SpecFlow.Parser;
using TechTalk.SpecFlow.Parser.Gherkin;
using TechTalk.SpecFlow.Utils;
using TechTalk.SpecFlow.Vs2010Integration.LanguageService;
using ScenarioBlock = TechTalk.SpecFlow.Parser.Gherkin.ScenarioBlock;

namespace TechTalk.SpecFlow.Vs2010Integration.AutoComplete
{
    [Export(typeof(ICompletionSourceProvider))]
    [ContentType("gherkin")]
    [Name("gherkinStepCompletion")]
    internal class GherkinStepCompletionSourceProvider : ICompletionSourceProvider
    {
        [Import]
        ISpecFlowServices SpecFlowServices = null;

        [Import]
        IGherkinLanguageServiceFactory GherkinLanguageServiceFactory = null;

        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer)
        {
            if (!SpecFlowServices.GetOptions().EnableIntelliSense)
                return null;

            return new GherkinStepCompletionSource(textBuffer, SpecFlowServices, GherkinLanguageServiceFactory.GetLanguageService(textBuffer));
        }
    }

    internal class GherkinStepCompletionSource : ICompletionSource
    {
        private bool disposed = false;
        private readonly ITextBuffer textBuffer;
        private readonly ISpecFlowServices specFlowServices;
        private readonly GherkinLanguageService languageService;

        public GherkinStepCompletionSource(ITextBuffer textBuffer, ISpecFlowServices specFlowServices, GherkinLanguageService languageService)
        {
            this.textBuffer = textBuffer;
            this.specFlowServices = specFlowServices;
            this.languageService = languageService;
        }

        public void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets)
        {
            if (disposed)
                throw new ObjectDisposedException("GherkinStepCompletionSource");

            ITextSnapshot snapshot = textBuffer.CurrentSnapshot;
            var triggerPoint = session.GetTriggerPoint(snapshot);
            if (triggerPoint == null)
                return;

            ScenarioBlock? scenarioBlock = GetCurrentScenarioBlock(triggerPoint.Value);
            if (scenarioBlock == null)
                return;

            IEnumerable<Completion> completions = GetCompletionsForBlock(scenarioBlock.Value);
            ITrackingSpan applicableTo = GetApplicableToSpan(snapshot, triggerPoint.Value);

            string displayName = string.Format("All {0} Steps", scenarioBlock);
            completionSets.Add(
                new CompletionSet(
                    displayName,
                    displayName,
                    applicableTo,
                    completions,
                    null));
        }

        private ITrackingSpan GetApplicableToSpan(ITextSnapshot snapshot, SnapshotPoint triggerPoint)
        {
            var line = triggerPoint.GetContainingLine();

            SnapshotPoint start = triggerPoint;
            while (start > line.Start && !char.IsWhiteSpace((start - 1).GetChar()))
            {
                start -= 1;
            }

            return snapshot.CreateTrackingSpan(new SnapshotSpan(start, line.End), SpanTrackingMode.EdgeInclusive);
        }

        private ScenarioBlock? GetCurrentScenarioBlock(SnapshotPoint triggerPoint)
        {
            var fileScope = languageService.GetFileScope(waitForParsingSnapshot: triggerPoint.Snapshot);
            if (fileScope == null)
                return null;

            var triggerLineNumber = triggerPoint.Snapshot.GetLineNumberFromPosition(triggerPoint.Position);
            var scenarioInfo = fileScope.ScenarioBlocks.LastOrDefault(si => si.KeywordLine < triggerLineNumber);
            if (scenarioInfo == null)
                return null;

            for (var lineNumer = triggerLineNumber; lineNumer > scenarioInfo.KeywordLine; lineNumer--)
            {
                StepKeyword? stepKeyword = GetStepKeyword(triggerPoint.Snapshot, lineNumer, fileScope.GherkinDialect);

                if (stepKeyword != null)
                {
                    var scenarioBlock = stepKeyword.Value.ToScenarioBlock();
                    if (scenarioBlock != null)
                        return scenarioBlock;
                }
            }

            return ScenarioBlock.Given;
        }

        private StepKeyword? GetStepKeyword(ITextSnapshot snapshot, int lineNumer, GherkinDialect gherkinDialect)
        {
            var word = GetFirstWordOfLine(snapshot, lineNumer);
            return gherkinDialect.GetStepKeyword(word);
        }

        private static string GetFirstWordOfLine(ITextSnapshot snapshot, int lineNumer)
        {
            var theLine = snapshot.GetLineFromLineNumber(lineNumer);
            return theLine.GetText().TrimStart().Split(' ')[0];
        }

        private IEnumerable<Completion> GetCompletionsForBlock(ScenarioBlock scenarioBlock)
        {
            var project = specFlowServices.GetProject(textBuffer);

            List<Completion> result = new List<Completion>();
            GetCompletionsFromProject(project, scenarioBlock, result);

            var specFlowProject = languageService.ProjectScope.SpecFlowProjectConfiguration;
            if (specFlowProject != null)
            {
                foreach (var assemblyName in specFlowProject.RuntimeConfiguration.AdditionalStepAssemblies)
                {
                    string simpleName = assemblyName.Split(new[] {',' }, 2)[0];

                    var stepProject = VsxHelper.FindProjectByAssemblyName(project.DTE, simpleName);
                    if (stepProject != null)
                        GetCompletionsFromProject(stepProject, scenarioBlock, result);
                }
            }

            result.Sort((c1, c2) => string.Compare(c1.DisplayText, c2.DisplayText));

            return result;
        }

        private void GetCompletionsFromProject(Project project, ScenarioBlock scenarioBlock, List<Completion> result)
        {
            foreach (ProjectItem projectItem in VsxHelper.GetAllProjectItem(project).Where(pi => pi.FileCodeModel != null))
            {
                FileCodeModel codeModel = projectItem.FileCodeModel;
                GetCompletitionsFromCodeElements(codeModel.CodeElements, scenarioBlock, result);
            }
        }

        private void GetCompletitionsFromCodeElements(CodeElements codeElements, ScenarioBlock scenarioBlock, List<Completion> result)
        {
            foreach (CodeElement codeElement in codeElements)
            {
                if (codeElement.Kind == vsCMElement.vsCMElementFunction)
                {
                    CodeFunction codeFunction = (CodeFunction)codeElement;
                    result.AddRange(
                        codeFunction.Attributes.Cast<CodeAttribute>()
                        .Where(attr => attr.FullName.Equals(string.Format("TechTalk.SpecFlow.{0}Attribute", scenarioBlock)))
                        .Select(attr => CreateCompletion(GetRecommendedStepText(attr.Value, codeFunction))));
                }
                else
                {
                    GetCompletitionsFromCodeElements(codeElement.Children, scenarioBlock, result);
                }
            }
        }

        private string UnmaskAttributeValue(string attrValue)
        {
            if (attrValue.StartsWith("@"))
            {
                return attrValue.Substring(2, attrValue.Length - 3).Replace("\"\"", "\"");
            }

            return attrValue.Substring(1, attrValue.Length - 2); //TODO: handle \ maskings
        }

        private string GetRecommendedStepText(string regexAttrValue, CodeFunction codeFunction)
        {
            string unmaskAttributeValue = UnmaskAttributeValue(regexAttrValue);

            var parameters = codeFunction.Parameters.Cast<CodeParameter>().Select(p => p.Name).ToArray();

            return RegexSampler.GetRegexSample(unmaskAttributeValue, parameters);
        }

        private Completion CreateCompletion(string stepText)
        {
            return new Completion(stepText);
        }

        public void Dispose()
        {
            disposed = true;
        }
    }
}

