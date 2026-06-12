using System;
using System.Collections.Generic;
using System.IO;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using WindowLayoutManager.Models;

namespace WindowLayoutManager.Services
{
    /// <summary>
    /// Captures the breakpoint set, and restores it by replacing all current file-location
    /// breakpoints with the snapshot's. Each operation runs on the UI thread (EnvDTE is STA COM).
    /// </summary>
    internal sealed class BreakpointService
    {
        public List<BreakpointEntry> CaptureAll(DTE dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<BreakpointEntry>();
            Breakpoints breakpoints = dte?.Debugger?.Breakpoints;
            if (breakpoints == null) return result;

            foreach (Breakpoint bp in breakpoints)
            {
                try
                {
                    string file = bp.File;
                    if (string.IsNullOrEmpty(file)) continue;   // only file-location breakpoints

                    result.Add(new BreakpointEntry
                    {
                        File = file,
                        Line = bp.FileLine,
                        Column = bp.FileColumn,
                        Condition = bp.Condition,
                        ConditionType = (int)bp.ConditionType,
                        HitCountTarget = bp.HitCountTarget,
                        HitCountType = (int)bp.HitCountType,
                        Enabled = bp.Enabled,
                        Tag = bp.Tag,
                        Language = bp.Language,
                    });
                }
                catch { /* skip a breakpoint we cannot read */ }
            }
            return result;
        }

        public void RestoreAll(DTE dte, List<BreakpointEntry> entries)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Breakpoints breakpoints = dte?.Debugger?.Breakpoints;
            if (breakpoints == null) return;

            // Collect existing file breakpoints first; deleting mutates the live collection.
            var existing = new List<Breakpoint>();
            foreach (Breakpoint bp in breakpoints)
            {
                try { if (!string.IsNullOrEmpty(bp.File)) existing.Add(bp); }
                catch { /* ignore unreadable */ }
            }
            foreach (Breakpoint bp in existing)
            {
                try { bp.Delete(); } catch { /* already gone */ }
            }

            if (entries == null) return;
            foreach (BreakpointEntry e in entries)
            {
                if (string.IsNullOrEmpty(e.File) || !File.Exists(e.File)) continue;
                try { AddOne(breakpoints, e); } catch { /* best-effort per breakpoint */ }
            }
        }

        private static void AddOne(Breakpoints breakpoints, BreakpointEntry e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            bool hasCondition = !string.IsNullOrEmpty(e.Condition);
            // dbgBreakpointConditionType has no "None"; an empty condition + WhenTrue is unconditional.
            dbgBreakpointConditionType conditionType = hasCondition
                ? (dbgBreakpointConditionType)e.ConditionType
                : dbgBreakpointConditionType.dbgBreakpointConditionTypeWhenTrue;
            string condition = hasCondition ? e.Condition : "";

            var hitCountType = (dbgHitCountType)e.HitCountType;
            int hitCount = e.HitCountTarget;
            if (hitCountType != dbgHitCountType.dbgHitCountTypeNone && hitCount < 1) hitCount = 1;

            Breakpoints added = breakpoints.Add(
                Function: "",
                File: e.File,
                Line: e.Line,
                Column: e.Column,
                Condition: condition,
                ConditionType: conditionType,
                Language: e.Language ?? "",
                Data: "",
                DataCount: 1,
                Address: "",
                HitCount: hitCount,
                HitCountType: hitCountType);

            if (added == null) return;
            foreach (Breakpoint nb in added)   // a source line can bind to multiple bound breakpoints
            {
                try
                {
                    nb.Enabled = e.Enabled;
                    if (!string.IsNullOrEmpty(e.Tag)) nb.Tag = e.Tag;
                }
                catch { /* ignore per-breakpoint property failures */ }
            }
        }
    }
}
