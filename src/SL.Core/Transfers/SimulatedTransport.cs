// SalmonLauncher (SL) — a Minecraft Java edition launcher
// Copyright (C) 2026 Hugo Mou
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE. See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with
// this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;

namespace SL.Core.Transfers;

/// <summary>
/// Stage 1 demo transport: produces a data stream at a given rate and can continue from a resume point (ResumeBytes),
/// injecting occasional failures deterministically to exercise the "single-source retry → multi-source fallback → eventual success" chain.
/// Failure injection rule: the last attempt of each source always succeeds ⇒ the task always completes eventually.
/// </summary>
public sealed class SimulatedTransport : ITransport
{
    public async Task<TransportResult> TransferAsync(TransferContext context, CancellationToken cancellation)
    {
        var task = context.Task;
        long total = task.Spec.BytesTotal;
        if (total <= 0)
        {
            // when no total is given, derive a fixed value from the Id hash between 8–48MB so repeated resumes stay consistent
            total = 8_000_000 + (Math.Abs((int)PseudoHash(task.Id)) % 40_000_000);
            total -= total % 1000;
        }

        long position = Math.Clamp(context.ResumeBytes, 0, total);
        double speed = task.Spec.SimulatedSpeed <= 0 ? 1_500_000 : task.Spec.SimulatedSpeed;
        speed *= 0.92 + pseudoRandom(context.SourceUrl) * 0.16; // ±8% jitter

        // deterministic failure injection: about 20% chance of failure on attempts that are not the last
        bool injectFailure =
            context.AttemptNumber < 3
            && PseudoHash(task.Id + "|" + context.SourceUrl + "|" + context.AttemptNumber) % 100 < 20;

        long partial = Math.Max(total / 7, 256_000);

        if (injectFailure)
        {
            // fail after a little progress so the source switch is visible in the UI
            await Simulate(position, Math.Min(position + partial, total), speed, context, cancellation).ConfigureAwait(false);
            return TransportResult.Fail("演示:模拟来源超时,建议切换镜像");
        }

        await Simulate(position, total, speed, context, cancellation).ConfigureAwait(false);
        return TransportResult.Ok(total);
    }

    private static async Task Simulate(long from, long to, double speed, TransferContext context, CancellationToken ct)
    {
        if (to <= from)
            return;

        context.Progress(from);

        const int chunk = 64_000;
        long position = from;
        while (position < to)
        {
            ct.ThrowIfCancellationRequested();
            var take = Math.Min(chunk, to - position);
            position += take;
            context.Progress(Math.Min(position, to));

            var ms = take * 1000.0 / Math.Max(speed, 50_000);
            if (ms >= 1)
                await Task.Delay((int)Math.Min(ms, 120), ct).ConfigureAwait(false);
        }
    }

    private static long PseudoHash(string s)
    {
        long h = 17;
        foreach (var ch in s)
            h = h * 31 + ch;
        return h;
    }

    private static double pseudoRandom(string s) => PseudoHash(s) % 1000 / 1000.0;
}
