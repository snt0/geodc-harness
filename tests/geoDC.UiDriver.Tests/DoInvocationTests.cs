using geoDC.UiDriver;

namespace geoDC.UiDriver.Tests;

[TestFixture]
public sealed class DoInvocationTests
{
    private const string ScriptPath = "dev/ui-scripts/test.ui";

    private static DoInvocation Parse(params string[] args)
    {
        return DoInvocation.Parse(args, _ => throw new FileNotFoundException("no script expected"));
    }

    private static DoInvocation ParseScript(string script, params string[] args)
    {
        return DoInvocation.Parse(["--file", ScriptPath, .. args], path =>
            path == ScriptPath ? script : throw new FileNotFoundException(path));
    }

    private static string? FailureCode(Action parse)
    {
        return Assert.Throws<DriverException>(() => parse())?.Code;
    }

    [Test]
    public void Inline_SplitsStepsOnPlusPlus()
    {
        DoInvocation invocation = Parse("--target", "sample", "click", "--id", "A", "++", "wait", "--id", "B");

        Assert.Multiple(() =>
        {
            Assert.That(invocation.Target, Is.EqualTo("sample"));
            Assert.That(invocation.ScriptPath, Is.Null);
            Assert.That(invocation.StepTokens, Is.EqualTo(new[]
            {
                new[] { "click", "--id", "A" },
                new[] { "wait", "--id", "B" }
            }));
        });
    }

    [Test]
    public void Inline_EmptyStep_IsBadArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FailureCode(() => Parse("click", "--id", "A", "++")), Is.EqualTo("bad-arguments"));
            Assert.That(FailureCode(() => Parse()), Is.EqualTo("bad-arguments"));
        });
    }

    [Test]
    public void Inline_RejectsVar_AndUnknownLeadingOptions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FailureCode(() => Parse("--var", "a=b", "click", "--id", "A")), Is.EqualTo("bad-arguments"));
            Assert.That(FailureCode(() => Parse("--timeout", "2", "click", "--id", "A")), Is.EqualTo("bad-arguments"));
        });
    }

    [Test]
    public void Script_OneStepPerLine_SkippingBlanksAndComments()
    {
        DoInvocation invocation = ParseScript("""
            # A comment
              # an indented comment

            click --id SetupTab --await SetupPage
            assert --id SaveButton --disabled
            """);

        Assert.Multiple(() =>
        {
            Assert.That(invocation.ScriptPath, Is.EqualTo(ScriptPath));
            Assert.That(invocation.StepTokens, Is.EqualTo(new[]
            {
                new[] { "click", "--id", "SetupTab", "--await", "SetupPage" },
                new[] { "assert", "--id", "SaveButton", "--disabled" }
            }));
        });
    }

    [Test]
    public void Script_QuotesGroupTokens_WithoutEscapes()
    {
        DoInvocation invocation = ParseScript("""
            select --id AttemptList --item "Pitch calibration (INITIAL)"
            assert --id HeadingInput --value-matches '^\d+\.5 °$'
            settext --id NoteInput --value 'it''s "quoted"'
            assert --id Empty --value ''
            """);

        Assert.That(invocation.StepTokens, Is.EqualTo(new[]
        {
            new[] { "select", "--id", "AttemptList", "--item", "Pitch calibration (INITIAL)" },
            new[] { "assert", "--id", "HeadingInput", "--value-matches", "^\\d+\\.5 °$" },
            new[] { "settext", "--id", "NoteInput", "--value", "it's \"quoted\"" },
            new[] { "assert", "--id", "Empty", "--value", "" }
        }));
    }

    [Test]
    public void Script_UnterminatedQuote_NamesTheLine()
    {
        DriverException? ex = Assert.Throws<DriverException>(() => ParseScript("click --id A\nselect --item \"open"));

        Assert.Multiple(() =>
        {
            Assert.That(ex?.Code, Is.EqualTo("bad-script"));
            Assert.That(ex?.Message, Does.StartWith($"{ScriptPath}:2:"));
        });
    }

    [Test]
    public void Script_SubstitutesVariables_WithCommandLineOverridingDefaults()
    {
        const string script = """
            @var attempt=Pitch calibration (INITIAL)
            select --id AttemptList --item '${attempt}'
            settext --id NameInput --value ${name}-copy
            """;

        DoInvocation invocation = ParseScript(script, "--var", "name=Roll", "--var", "attempt=Roll (INITIAL)");
        DoInvocation defaults = ParseScript(script, "--var", "name=Roll");

        Assert.Multiple(() =>
        {
            Assert.That(invocation.StepTokens[0][^1], Is.EqualTo("Roll (INITIAL)"));
            Assert.That(invocation.StepTokens[1][^1], Is.EqualTo("Roll-copy"));
            Assert.That(defaults.StepTokens[0][^1], Is.EqualTo("Pitch calibration (INITIAL)"));
        });
    }

    [Test]
    public void Script_UndefinedVariable_FailsBeforeAnyStep()
    {
        DriverException? ex = Assert.Throws<DriverException>(() => ParseScript("click --id ${missing}"));

        Assert.Multiple(() =>
        {
            Assert.That(ex?.Code, Is.EqualTo("bad-script"));
            Assert.That(ex?.Message, Does.Contain("--var missing=<value>"));
        });
    }

    [Test]
    public void Script_VarTheScriptNeverUses_IsBadScript()
    {
        DriverException? ex = Assert.Throws<DriverException>(() =>
            ParseScript("@var page=A\nclick --id ${page}", "--var", "pgae=B"));

        Assert.Multiple(() =>
        {
            Assert.That(ex?.Code, Is.EqualTo("bad-script"));
            Assert.That(ex?.Message, Does.Contain("pgae").And.Contain("takes page"));
        });
    }

    [Test]
    public void RebuildArguments_VarTheScriptNeverUses_FailsBeforeAnythingRuns()
    {
        Assert.That(FailureCode(() => RebuildArguments.Split(["rebuild", "--then", "lib/s", "--var", "pgae=B"],
            _ => "@var page=A\nclick --id ${page}")), Is.EqualTo("bad-script"));
    }

    [Test]
    public void Script_BadVariableArguments_AreRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FailureCode(() => ParseScript("click --id A", "--var", "novalue")),
                Is.EqualTo("bad-arguments"));
            Assert.That(FailureCode(() => ParseScript("click --id A", "--var", "a=1", "--var", "a=2")),
                Is.EqualTo("bad-arguments"));
        });
    }

    [Test]
    public void Script_TargetDirective_SetsTarget_AndConflictsWithADifferentOne()
    {
        const string script = "@target sample\nclick --id OverlaysNavItem";

        Assert.Multiple(() =>
        {
            Assert.That(ParseScript(script).Target, Is.EqualTo("sample"));
            Assert.That(ParseScript(script, "--target", "SAMPLE").Target, Is.EqualTo("SAMPLE"));
            Assert.That(FailureCode(() => ParseScript(script, "--target", "app")), Is.EqualTo("bad-arguments"));
        });
    }

    [Test]
    public void Script_DirectivesMustPrecedeSteps_AndBeKnown()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FailureCode(() => ParseScript("click --id A\n@target app")), Is.EqualTo("bad-script"));
            Assert.That(FailureCode(() => ParseScript("@target app\n@target app\nclick --id A")),
                Is.EqualTo("bad-script"));
            Assert.That(FailureCode(() => ParseScript("@timeout 5\nclick --id A")), Is.EqualTo("bad-script"));
        });
    }

    [Test]
    public void Script_RejectsPlusPlus_EmptyScripts_AndInlineStepsAlongside()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FailureCode(() => ParseScript("click --id A ++ click --id B")), Is.EqualTo("bad-script"));
            Assert.That(FailureCode(() => ParseScript("# only a comment\n")), Is.EqualTo("bad-script"));
            Assert.That(FailureCode(() => DoInvocation.Parse(["--file", ScriptPath, "click", "--id", "A"],
                _ => "click --id B")), Is.EqualTo("bad-arguments"));
        });
    }

    [Test]
    public void Inline_FindAndInteractableTree_ParseAsSteps()
    {
        DoInvocation invocation = Parse("--target", "sample", "tree", "--interactable", "--scope", "PopupPanel", "++",
            "find", "--role", "Button", "--scope", "PopupFooterPanel");

        CliArgs tree = new(invocation.StepTokens[0]);
        CliArgs find = new(invocation.StepTokens[1]);
        Assert.Multiple(() =>
        {
            Assert.That(tree.Verb, Is.EqualTo("tree"));
            Assert.That(tree.Value("--scope"), Is.EqualTo("PopupPanel"));
            Assert.That(TreeOptions.Parse(tree), Is.EqualTo(new TreeOptions(true, null)));
            Assert.That(find.Verb, Is.EqualTo("find"));
            Assert.That(find.Value("--scope"), Is.EqualTo("PopupFooterPanel"));
            Assert.That(FindCriteria.Parse(find, ["Button"]), Is.EqualTo(new FindCriteria(null, "Button", null, null)));
        });
    }

    [Test]
    public void Script_FindAndInteractableTree_ParseAsSteps()
    {
        DoInvocation invocation = ParseScript("""
            tree --interactable --depth 8
            find --name 'Save as' --pattern Invoke
            """);

        Assert.Multiple(() =>
        {
            Assert.That(TreeOptions.Parse(new CliArgs(invocation.StepTokens[0])), Is.EqualTo(new TreeOptions(true, 8)));
            Assert.That(FindCriteria.Parse(new CliArgs(invocation.StepTokens[1]), ["Button"]),
                Is.EqualTo(new FindCriteria("Save as", null, "Invoke", null)));
        });
    }

    [Test]
    public void Inline_ActionThenTreeDiff_ParseAsSteps_SharingTheSnapshotKey()
    {
        DoInvocation invocation = Parse("--target", "sample", "tree", "--scope", "InputsPage", "++", "click", "--id",
            "ShowGridToggle", "++", "tree", "--diff", "--scope", "InputsPage");

        CliArgs before = new(invocation.StepTokens[0]);
        CliArgs click = new(invocation.StepTokens[1]);
        CliArgs diff = new(invocation.StepTokens[2]);
        Assert.Multiple(() =>
        {
            Assert.That(invocation.StepTokens, Has.Count.EqualTo(3));
            Assert.That(click.Verb, Is.EqualTo("click"));
            Assert.That(diff.Verb, Is.EqualTo("tree"));
            Assert.That(TreeOptions.Parse(diff),
                Is.EqualTo(new TreeOptions(false, TreeOptions.DefaultDepth, true)));
            Assert.That(TreeSnapshotKey.From("sample", diff, TreeOptions.Parse(diff)),
                Is.EqualTo(TreeSnapshotKey.From("sample", before, TreeOptions.Parse(before))));
        });
    }

    [Test]
    public void Script_TreeDiffWithBounds_ParsesAsAStep()
    {
        DoInvocation invocation = ParseScript("""
            click --id PopupBlockingButton --await PopupPanel
            tree --diff --bounds --interactable
            """);

        Assert.That(TreeOptions.Parse(new CliArgs(invocation.StepTokens[1])),
            Is.EqualTo(new TreeOptions(true, null, true, true)));
    }

    [Test]
    public void Script_Unreadable_IsScriptNotFound()
    {
        Assert.That(FailureCode(() => DoInvocation.Parse(["--file", "missing.ui"],
            path => throw new FileNotFoundException(path))), Is.EqualTo("script-not-found"));
    }

    private static readonly string[] DoVerbs =
    [
        "tree", "find", "audit", "screenshot", "click", "settext", "select", "scroll", "scrollto", "wait", "assert",
        "key", "settle", "logs", "pause"
    ];

    private static IEnumerable<string> CheckedInScripts()
    {
        string root = Path.Combine(TestPaths.HarnessRoot, "dev", "ui-scripts");
        return Directory.EnumerateFiles(root, "*.ui", SearchOption.AllDirectories)
            .Select(path => "dev/ui-scripts/" + Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
    }

    [TestCaseSource(nameof(CheckedInScripts))]
    public void CheckedInScripts_ExpandToValidSteps_EndingInAnAssert(string script)
    {
        string root = TestPaths.HarnessRoot;

        DoInvocation invocation = DoInvocation.Parse(["--file", script],
            path => File.ReadAllText(Path.Combine(root, path)));

        Assert.Multiple(() =>
        {
            Assert.That(invocation.Target, Is.EqualTo("app").Or.EqualTo("sample"), "every script names its @target");
            Assert.That(invocation.StepTokens, Is.Not.Empty);
            Assert.That(invocation.StepTokens.Select(step => step[0]), Is.All.AnyOf(DoVerbs));
            Assert.That(invocation.StepTokens.Select(step => new CliArgs(step)), Is.All.Not.Null,
                "every step must parse with the step grammar");
            Assert.That(invocation.StepTokens[^1][0], Is.EqualTo("assert"), "a script proves the state it reaches");
        });
    }

    // --- run ---

    private static DoInvocation ParseWith(IReadOnlyDictionary<string, string> files, params string[] args)
    {
        return DoInvocation.Parse(args, path =>
            files.TryGetValue(path, out string? text) ? text : throw new FileNotFoundException(path));
    }

    [Test]
    public void Run_ReplacesTheStepWithTheCalledScriptsSteps_AndRecordsWhereEachCameFrom()
    {
        Dictionary<string, string> files = new()
        {
            ["dev/ui-scripts/top.ui"] = "@target app\nclick --id A\nrun lib/goto page=Calibration\nassert --id Z",
            ["dev/ui-scripts/lib/goto.ui"] = "@target app\n# go\nclick --id ${page}Tab --await ${page}Page"
        };

        DoInvocation invocation = ParseWith(files, "--file", "dev/ui-scripts/top.ui");

        Assert.Multiple(() =>
        {
            Assert.That(invocation.StepTokens, Is.EqualTo(new[]
            {
                new[] { "click", "--id", "A" },
                new[] { "click", "--id", "CalibrationTab", "--await", "CalibrationPage" },
                new[] { "assert", "--id", "Z" }
            }));
            Assert.That(invocation.Steps[0].From, Is.EqualTo(new[] { "dev/ui-scripts/top.ui:2" }));
            Assert.That(invocation.Steps[1].From, Is.EqualTo(new[]
            {
                "dev/ui-scripts/top.ui:3", "dev/ui-scripts/lib/goto.ui:3"
            }));
        });
    }

    [Test]
    public void Run_Inline_ExpandsWithoutAnInlineEntry()
    {
        Dictionary<string, string> files = new() { ["dev/ui-scripts/lib/a.ui"] = "click --id A" };

        DoInvocation invocation = ParseWith(files, "run", "lib/a", "++", "assert", "--id", "B");

        Assert.Multiple(() =>
        {
            Assert.That(invocation.StepTokens, Is.EqualTo(new[]
            {
                new[] { "click", "--id", "A" }, new[] { "assert", "--id", "B" }
            }));
            Assert.That(invocation.Steps[0].From, Is.EqualTo(new[] { "dev/ui-scripts/lib/a.ui:1" }));
            Assert.That(invocation.Steps[1].From, Is.Empty);
        });
    }

    [Test]
    public void Run_CalledScriptSeesOnlyItsArgumentsAndDefaults()
    {
        Dictionary<string, string> files = new()
        {
            ["dev/ui-scripts/top.ui"] = "@var page=Outer\nrun lib/goto item=x\nclick --id ${page}",
            ["dev/ui-scripts/lib/goto.ui"] = "@var page=Inner\nselect --id ${page} --item ${item}"
        };

        DoInvocation invocation = ParseWith(files, "--file", "dev/ui-scripts/top.ui", "--var", "page=Given");

        Assert.That(invocation.StepTokens, Is.EqualTo(new[]
        {
            new[] { "select", "--id", "Inner", "--item", "x" }, new[] { "click", "--id", "Given" }
        }));
    }

    [Test]
    public void Run_CallerVariablesSubstituteIntoArguments_AndQuotedValuesKeepSpaces()
    {
        Dictionary<string, string> files = new()
        {
            ["dev/ui-scripts/top.ui"] = "@var name=Pitch\nrun lib/open 'attempt=${name} calibration (INITIAL)'",
            ["dev/ui-scripts/lib/open.ui"] = "select --id AttemptList --item '${attempt}'"
        };

        DoInvocation invocation = ParseWith(files, "--file", "dev/ui-scripts/top.ui");

        Assert.That(invocation.StepTokens[0][^1], Is.EqualTo("Pitch calibration (INITIAL)"));
    }

    [Test]
    public void Run_UnusedOrMissingVariable_IsBadScript_NamingTheCaller()
    {
        Dictionary<string, string> files = new()
        {
            ["dev/ui-scripts/unused.ui"] = "run lib/goto page=A typo=B",
            ["dev/ui-scripts/missing.ui"] = "run lib/goto",
            ["dev/ui-scripts/lib/goto.ui"] = "click --id ${page}"
        };

        DriverException? unused = Assert.Throws<DriverException>(() =>
            ParseWith(files, "--file", "dev/ui-scripts/unused.ui"));
        DriverException? missing = Assert.Throws<DriverException>(() =>
            ParseWith(files, "--file", "dev/ui-scripts/missing.ui"));

        Assert.Multiple(() =>
        {
            Assert.That(unused?.Code, Is.EqualTo("bad-script"));
            Assert.That(unused?.Message, Does.Contain("typo").And.Contain("takes page"));
            Assert.That(missing?.Code, Is.EqualTo("bad-script"));
            Assert.That(missing?.Message, Does.Contain("dev/ui-scripts/lib/goto.ui:1")
                .And.Contain("run from dev/ui-scripts/missing.ui:1"));
        });
    }

    [Test]
    public void Run_ErrorsInNestedScripts_NameTheWholeChain()
    {
        Dictionary<string, string> files = new()
        {
            ["dev/ui-scripts/top.ui"] = "click --id A\nrun lib/a",
            ["dev/ui-scripts/lib/a.ui"] = "run ./b",
            ["dev/ui-scripts/lib/b.ui"] = "select --item \"open"
        };

        DriverException? ex = Assert.Throws<DriverException>(() =>
            ParseWith(files, "--file", "dev/ui-scripts/top.ui"));

        Assert.That(ex?.Message, Does.StartWith("dev/ui-scripts/lib/b.ui:1: unterminated double quote")
            .And.EndWith("(run from dev/ui-scripts/lib/a.ui:1 < dev/ui-scripts/top.ui:2)"));
    }

    [Test]
    public void Run_Recursion_IsBadScript_WithTheCycle()
    {
        Dictionary<string, string> files = new()
        {
            ["dev/ui-scripts/a.ui"] = "run lib/b",
            ["dev/ui-scripts/lib/b.ui"] = "run ../a.ui"
        };

        DriverException? ex = Assert.Throws<DriverException>(() =>
            ParseWith(files, "--file", "dev/ui-scripts/a.ui"));

        Assert.Multiple(() =>
        {
            Assert.That(ex?.Code, Is.EqualTo("bad-script"));
            Assert.That(ex?.Message, Does.Contain(
                "dev/ui-scripts/a.ui -> dev/ui-scripts/lib/b.ui -> dev/ui-scripts/a.ui"));
        });
    }

    [Test]
    public void Run_TargetMustAgreeWithTheInvocation()
    {
        Dictionary<string, string> files = new()
        {
            ["dev/ui-scripts/lib/sample-page.ui"] = "@target sample\nclick --id A",
            ["dev/ui-scripts/app.ui"] = "@target app\nrun lib/sample-page"
        };

        Assert.Multiple(() =>
        {
            Assert.That(FailureCode(() => ParseWith(files, "run", "lib/sample-page")), Is.EqualTo("bad-script"));
            Assert.That(FailureCode(() => ParseWith(files, "--file", "dev/ui-scripts/app.ui")),
                Is.EqualTo("bad-script"));
            Assert.That(ParseWith(files, "--target", "SAMPLE", "run", "lib/sample-page").StepTokens,
                Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Run_BadSyntaxAndUnreadableScripts_Fail()
    {
        Dictionary<string, string> files = new() { ["dev/ui-scripts/lib/a.ui"] = "click --id ${x}" };

        Assert.Multiple(() =>
        {
            Assert.That(FailureCode(() => ParseWith(files, "run")), Is.EqualTo("bad-arguments"));
            Assert.That(FailureCode(() => ParseWith(files, "run", "--id", "A")), Is.EqualTo("bad-arguments"));
            Assert.That(FailureCode(() => ParseWith(files, "run", "lib/a", "novalue")), Is.EqualTo("bad-arguments"));
            Assert.That(FailureCode(() => ParseWith(files, "run", "lib/a", "x=1", "x=2")),
                Is.EqualTo("bad-arguments"));
            Assert.That(FailureCode(() => ParseWith(files, "run", "lib/missing")), Is.EqualTo("script-not-found"));
            Assert.That(ParseWith(files, "run", "lib/a", "x=1").StepTokens[0][^1], Is.EqualTo("1"));
        });
    }

    [TestCase("lib/goto", null, "dev/ui-scripts/lib/goto.ui")]
    [TestCase("lib/goto.ui", null, "dev/ui-scripts/lib/goto.ui")]
    [TestCase("lib\\goto", null, "dev/ui-scripts/lib/goto.ui")]
    [TestCase("./b", "dev/ui-scripts/lib/a.ui", "dev/ui-scripts/lib/b.ui")]
    [TestCase("../top", "dev/ui-scripts/lib/a.ui", "dev/ui-scripts/top.ui")]
    [TestCase("./local", null, "local.ui")]
    [TestCase("lib/goto", "elsewhere/a.ui", "dev/ui-scripts/lib/goto.ui")]
    [TestCase("dev/ui-scripts/lib/goto.ui", null, "dev/ui-scripts/lib/goto.ui")]
    [TestCase("dev\\ui-scripts\\top", null, "dev/ui-scripts/top.ui")]
    public void ResolveScriptPath_UsesTheScriptRootUnlessRelativeToTheCaller(string name, string? caller,
        string expected)
    {
        Assert.That(DoInvocation.ResolveScriptPath(name, caller), Is.EqualTo(expected));
    }

    // --- --interactive ---

    [Test]
    public void Interactive_IsALeadingFlag_ForInlineAndScriptRuns()
    {
        Dictionary<string, string> files = new() { ["dev/ui-scripts/check.ui"] = "pause --message 'Look'" };

        DoInvocation inline = ParseWith(files, "--interactive", "pause", "--message", "Look");
        DoInvocation script = ParseWith(files, "--file", "dev/ui-scripts/check.ui", "--interactive");
        DoInvocation unattended = ParseWith(files, "--file", "dev/ui-scripts/check.ui");

        Assert.Multiple(() =>
        {
            Assert.That(inline.Interactive, Is.True);
            Assert.That(inline.StepTokens, Is.EqualTo(new[] { new[] { "pause", "--message", "Look" } }));
            Assert.That(script.Interactive, Is.True);
            Assert.That(unattended.Interactive, Is.False);
        });
    }

    [Test]
    public void RebuildArguments_PassesInteractiveToTheScript_AndRejectsItWithoutThen()
    {
        (string[] rebuild, DoInvocation? then) = RebuildArguments.Split(
            ["rebuild", "--interactive", "--then", "lib/s", "--passive"], _ => "pause");

        Assert.Multiple(() =>
        {
            Assert.That(rebuild, Is.EqualTo(new[] { "rebuild", "--passive" }));
            Assert.That(then?.Interactive, Is.True);
            Assert.That(FailureCode(() => RebuildArguments.Split(["rebuild", "--interactive"], _ => "")),
                Is.EqualTo("bad-arguments"));
        });
    }

    // --- rebuild --then ---

    [Test]
    public void RebuildArguments_SplitsOffTheScriptAndItsVariables()
    {
        Dictionary<string, string> files = new()
        {
            ["dev/ui-scripts/lib/s.ui"] = "@target sample\n@var a=1\nclick --id ${a}${b}"
        };

        (string[] rebuild, DoInvocation? then) = RebuildArguments.Split(
            ["rebuild", "--target", "sample", "--then", "lib/s", "--var", "a=X", "--passive", "--var", "b=Y"],
            path => files[path]);

        Assert.Multiple(() =>
        {
            Assert.That(rebuild, Is.EqualTo(new[] { "rebuild", "--target", "sample", "--passive" }));
            Assert.That(then?.Target, Is.EqualTo("sample"));
            Assert.That(then?.ScriptPath, Is.EqualTo("dev/ui-scripts/lib/s.ui"));
            Assert.That(then?.StepTokens, Is.EqualTo(new[] { new[] { "click", "--id", "XY" } }));
        });
    }

    [Test]
    public void RebuildArguments_WithoutThen_LeavesTheArgumentsAlone_AndRejectsVar()
    {
        (string[] rebuild, DoInvocation? then) = RebuildArguments.Split(["rebuild", "--active"],
            _ => throw new FileNotFoundException());

        Assert.Multiple(() =>
        {
            Assert.That(rebuild, Is.EqualTo(new[] { "rebuild", "--active" }));
            Assert.That(then, Is.Null);
            Assert.That(FailureCode(() => RebuildArguments.Split(["rebuild", "--var", "a=1"], _ => "")),
                Is.EqualTo("bad-arguments"));
            Assert.That(FailureCode(() => RebuildArguments.Split(["rebuild", "--then"], _ => "")),
                Is.EqualTo("bad-arguments"));
        });
    }

    [Test]
    public void RebuildArguments_ScriptForTheOtherTarget_FailsBeforeAnythingRuns()
    {
        Assert.That(FailureCode(() => RebuildArguments.Split(["rebuild", "--then", "s.ui"],
            _ => "@target sample\nclick --id A")), Is.EqualTo("bad-arguments"));
    }
}