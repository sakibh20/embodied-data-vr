#!/usr/bin/env python3
"""
Embodied-Data-VR — study analysis pipeline (M16).

Parses per-participant session logs (the JSON written by SessionController.WriteLog)
into tidy per-trial rows and per-condition summaries.

Usage:
    python analyze_study.py <logs_dir> [--out <out_dir>] [--datasets datasets.json]

Inputs:
    <logs_dir>       folder containing participant_*.json files.
    --datasets       optional JSON mapping datasetName -> [values...]. When provided,
                     a normalized retrace-accuracy proxy is computed (see notes).

Outputs (written to --out, default alongside logs):
    trials.csv       one row per trial (recall score, phase durations, retrace metrics).
    conditions.csv   per-condition aggregate (means across all trials/participants).
    A summary is also printed to stdout.

Notes on retrace accuracy:
    The log stores the participant's retrace path as world (x, z) samples but NOT the
    ground-truth graph geometry, so absolute spatial accuracy cannot be recovered from
    the log alone. This script always reports descriptive retrace metrics (path length,
    duration, lateral/forward spread). If --datasets is supplied it additionally reports
    a shape-correlation proxy: Pearson r between the retrace's lateral profile (resampled
    over forward progress) and the dataset's value profile. Treat this as a rough proxy;
    for rigorous accuracy, log the value profile or graph mapping per trial.
"""
import argparse
import csv
import glob
import json
import math
import os
import statistics as stats


def recall_score(trial):
    qs = trial.get("questions", [])
    correct = sum(1 for q in qs if q.get("answeredIndex", -1) == q.get("correctIndex", -2))
    return correct, len(qs)


def path_metrics(retrace):
    """Descriptive metrics of a retrace path (list of {t,x,z})."""
    n = len(retrace)
    if n == 0:
        return {"samples": 0, "length": 0.0, "duration": 0.0,
                "x_range": 0.0, "z_range": 0.0}
    xs = [p["x"] for p in retrace]
    zs = [p["z"] for p in retrace]
    ts = [p.get("t", 0.0) for p in retrace]
    length = 0.0
    for i in range(1, n):
        dx = retrace[i]["x"] - retrace[i - 1]["x"]
        dz = retrace[i]["z"] - retrace[i - 1]["z"]
        length += math.hypot(dx, dz)
    return {
        "samples": n,
        "length": length,
        "duration": max(ts) - min(ts) if ts else 0.0,
        "x_range": max(xs) - min(xs),
        "z_range": max(zs) - min(zs),
    }


def pearson(a, b):
    if len(a) < 2 or len(a) != len(b):
        return float("nan")
    ma, mb = sum(a) / len(a), sum(b) / len(b)
    num = sum((x - ma) * (y - mb) for x, y in zip(a, b))
    da = math.sqrt(sum((x - ma) ** 2 for x in a))
    db = math.sqrt(sum((y - mb) ** 2 for y in b))
    return num / (da * db) if da > 0 and db > 0 else float("nan")


def resample_lateral(retrace, k):
    """Resample lateral offset (x) over forward progress (z) into k bins."""
    if len(retrace) < 2:
        return None
    zs = [p["z"] for p in retrace]
    xs = [p["x"] for p in retrace]
    z0, z1 = min(zs), max(zs)
    if z1 - z0 == 0:
        return None
    pts = sorted(zip(zs, xs))
    out = []
    for i in range(k):
        target = z0 + (z1 - z0) * i / (k - 1)
        nearest = min(pts, key=lambda p: abs(p[0] - target))
        out.append(nearest[1])
    return out


def normalize(vals):
    lo, hi = min(vals), max(vals)
    if hi - lo == 0:
        return [0.0 for _ in vals]
    return [(v - lo) / (hi - lo) for v in vals]


def retrace_shape_corr(retrace, values):
    """Proxy: correlation between retrace lateral profile and dataset value profile."""
    k = len(values)
    if k < 2:
        return float("nan")
    lat = resample_lateral(retrace, k)
    if lat is None:
        return float("nan")
    return pearson(normalize(lat), normalize(values))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("logs_dir")
    ap.add_argument("--out", default=None)
    ap.add_argument("--datasets", default=None)
    args = ap.parse_args()

    out_dir = args.out or args.logs_dir
    os.makedirs(out_dir, exist_ok=True)

    datasets = {}
    if args.datasets and os.path.exists(args.datasets):
        with open(args.datasets) as f:
            datasets = json.load(f)

    files = sorted(glob.glob(os.path.join(args.logs_dir, "participant_*.json")))
    if not files:
        print(f"No participant_*.json files found in {args.logs_dir}")
        return

    rows = []
    for path in files:
        with open(path) as f:
            log = json.load(f)
        pid = log.get("participantId")
        for t in log.get("trials", []):
            correct, total = recall_score(t)
            pm = path_metrics(t.get("retracePath", []))
            ds = t.get("datasetName", "")
            corr = float("nan")
            if ds in datasets:
                corr = retrace_shape_corr(t.get("retracePath", []), datasets[ds])
            rows.append({
                "participant": pid,
                "order": t.get("orderIndex"),
                "condition": t.get("condition"),
                "dataset": ds,
                "recall_correct": correct,
                "recall_total": total,
                "recall_pct": (100.0 * correct / total) if total else 0.0,
                "walk_s": round(t.get("walkSeconds", 0.0), 3),
                "distractor_s": round(t.get("distractorSeconds", 0.0), 3),
                "retrace_s": round(t.get("retraceSeconds", 0.0), 3),
                "retrace_samples": pm["samples"],
                "retrace_len": round(pm["length"], 3),
                "retrace_x_range": round(pm["x_range"], 3),
                "retrace_z_range": round(pm["z_range"], 3),
                "retrace_shape_r": round(corr, 3) if not math.isnan(corr) else "",
            })

    trials_csv = os.path.join(out_dir, "trials.csv")
    with open(trials_csv, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)

    conds = {}
    for r in rows:
        conds.setdefault(r["condition"], []).append(r)

    cond_rows = []
    for cond, rs in sorted(conds.items()):
        pcts = [r["recall_pct"] for r in rs]
        lens = [r["retrace_len"] for r in rs]
        cond_rows.append({
            "condition": cond,
            "n_trials": len(rs),
            "recall_pct_mean": round(stats.mean(pcts), 1),
            "recall_pct_sd": round(stats.pstdev(pcts), 1) if len(pcts) > 1 else 0.0,
            "retrace_len_mean": round(stats.mean(lens), 3),
        })

    conds_csv = os.path.join(out_dir, "conditions.csv")
    with open(conds_csv, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(cond_rows[0].keys()))
        w.writeheader()
        w.writerows(cond_rows)

    n_part = len(files)
    print(f"Parsed {n_part} participant file(s), {len(rows)} trials.\n")
    print("Per-condition recall (mean % correct):")
    for c in cond_rows:
        print(f"  {c['condition']:16s} n={c['n_trials']:2d}  "
              f"recall={c['recall_pct_mean']:5.1f}% (sd {c['recall_pct_sd']:.1f})  "
              f"retrace_len={c['retrace_len_mean']:.2f}")
    print(f"\nWrote:\n  {trials_csv}\n  {conds_csv}")


if __name__ == "__main__":
    main()
