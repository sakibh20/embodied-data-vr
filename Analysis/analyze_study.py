#!/usr/bin/env python3
"""
Embodied-Data-VR -- study analysis pipeline (M16, updated for CSV logging).

SessionController writes its raw data straight to CSV -- trials.csv,
value_questions.csv, retracing_log.csv -- one copy of each inside every
participant's own subfolder (<study_data_dir>/p01/trials.csv, <study_data_dir>/p02/
trials.csv, ...) rather than one shared file for the whole study, so each
participant's data is self-contained and can be copied/backed up/deleted on its
own. This script reads those raw CSVs -- across every participant subfolder it
finds -- and derives per-trial retrace metrics and per-condition summaries from
them; it no longer reads any JSON. For backward compatibility it also reads a
flat <study_data_dir>/trials.csv etc. directly, if present, from data collected
before this per-participant layout existed.

Usage:
    python analyze_study.py <study_data_dir> [--out <out_dir>] [--datasets datasets.json]

Inputs (found under <study_data_dir>/<participant>/, as written by SessionController):
    trials.csv          one row per trial: participant_id, condition, audio_cue,
                         visual_cue, presentation_order, trial_in_condition,
                         dataset_id, timestamps, phase durations, recall_score,
                         recall_total.
    retracing_log.csv   one row per retrace sample (participant_id, presentation_order,
                         t_seconds, head_x, head_y, head_z, ...) -- many rows per trial.
    value_questions.csv one row per recall question (not required for the metrics
                         below, since trials.csv already carries recall_score/total,
                         but available for per-question-type breakdowns).
    --datasets          optional JSON mapping dataset_id -> [values...]. When
                         provided, a normalized retrace-accuracy proxy is computed
                         (see notes).

Outputs (written to --out, default <study_data_dir>/analysis so they never collide
with Unity's own raw trials.csv sitting alongside them):
    analysis_trials.csv      one row per trial: every trials.csv column plus the
                              derived retrace metrics (and recall_pct).
    analysis_conditions.csv  per-condition aggregate (means across all trials).
    A summary is also printed to stdout.

Notes on retrace accuracy:
    retracing_log.csv stores the participant's retrace path as world (x, y, z)
    samples but not the ground-truth graph geometry, so absolute spatial accuracy
    can't be recovered from the log alone. This script always reports descriptive
    retrace metrics (path length, duration, lateral/forward spread). If --datasets
    is supplied it additionally reports a shape-correlation proxy: Pearson r
    between the retrace's lateral profile (resampled over forward progress) and
    the dataset's value profile. Treat this as a rough proxy; for rigorous
    accuracy, log the value profile or graph mapping per trial.
"""
import argparse
import csv
import glob
import json
import math
import os
import statistics as stats


def read_csv_rows(path):
    if not os.path.exists(path):
        return []
    with open(path, newline="") as f:
        return list(csv.DictReader(f))


def read_all_rows(study_data_dir, filename):
    """
    Reads rows for `filename` (e.g. "trials.csv") from every participant's own
    subfolder under study_data_dir (study_data_dir/p01/trials.csv, p02/..., etc. --
    how SessionController writes them now). Also reads a flat
    study_data_dir/<filename> directly, if present, for backward compatibility with
    data collected before per-participant folders existed. The "analysis" output
    subfolder (this script's own --out default) is skipped so a re-run doesn't try
    to read its own outputs back in as a "participant".
    """
    rows = []
    rows.extend(read_csv_rows(os.path.join(study_data_dir, filename)))
    for entry in sorted(glob.glob(os.path.join(study_data_dir, "*"))):
        if not os.path.isdir(entry) or os.path.basename(entry) == "analysis":
            continue
        rows.extend(read_csv_rows(os.path.join(entry, filename)))
    return rows


def trial_key(row):
    """(participant_id, presentation_order) uniquely identifies a trial."""
    return (row.get("participant_id"), row.get("presentation_order"))


def path_metrics(samples):
    """Descriptive metrics of a retrace path (list of retracing_log.csv rows,
    already filtered to one trial)."""
    n = len(samples)
    if n == 0:
        return {"samples": 0, "length": 0.0, "duration": 0.0,
                "x_range": 0.0, "z_range": 0.0}
    samples = sorted(samples, key=lambda s: float(s.get("t_seconds", 0.0) or 0.0))
    xs = [float(s["head_x"]) for s in samples]
    zs = [float(s["head_z"]) for s in samples]
    ts = [float(s.get("t_seconds", 0.0) or 0.0) for s in samples]
    length = 0.0
    for i in range(1, n):
        dx = xs[i] - xs[i - 1]
        dz = zs[i] - zs[i - 1]
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


def resample_lateral(samples, k):
    """Resample lateral offset (x) over forward progress (z) into k bins."""
    if len(samples) < 2:
        return None
    zs = [float(s["head_z"]) for s in samples]
    xs = [float(s["head_x"]) for s in samples]
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


def retrace_shape_corr(samples, values):
    """Proxy: correlation between retrace lateral profile and dataset value profile."""
    k = len(values)
    if k < 2:
        return float("nan")
    lat = resample_lateral(samples, k)
    if lat is None:
        return float("nan")
    return pearson(normalize(lat), normalize(values))


def to_int(row, key, default=0):
    try:
        return int(row.get(key, default) or default)
    except (TypeError, ValueError):
        return default


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("study_data_dir")
    ap.add_argument("--out", default=None)
    ap.add_argument("--datasets", default=None)
    args = ap.parse_args()

    out_dir = args.out or os.path.join(args.study_data_dir, "analysis")
    os.makedirs(out_dir, exist_ok=True)

    datasets = {}
    if args.datasets and os.path.exists(args.datasets):
        with open(args.datasets) as f:
            datasets = json.load(f)

    trials = read_all_rows(args.study_data_dir, "trials.csv")
    if not trials:
        print(f"No trials.csv found (or it's empty) under {args.study_data_dir} "
              f"(checked the flat layout and every participant subfolder)")
        return
    retrace_rows = read_all_rows(args.study_data_dir, "retracing_log.csv")

    retrace_by_trial = {}
    for r in retrace_rows:
        retrace_by_trial.setdefault(trial_key(r), []).append(r)

    rows = []
    for t in trials:
        samples = retrace_by_trial.get(trial_key(t), [])
        pm = path_metrics(samples)
        ds = t.get("dataset_id", "")
        corr = float("nan")
        if ds in datasets:
            corr = retrace_shape_corr(samples, datasets[ds])

        recall_total = to_int(t, "recall_total")
        recall_correct = to_int(t, "recall_score")

        row = dict(t)   # keep every raw trials.csv column
        row.update({
            "recall_pct": round(100.0 * recall_correct / recall_total, 1) if recall_total else 0.0,
            "retrace_samples": pm["samples"],
            "retrace_len": round(pm["length"], 3),
            "retrace_x_range": round(pm["x_range"], 3),
            "retrace_z_range": round(pm["z_range"], 3),
            "retrace_shape_r": round(corr, 3) if not math.isnan(corr) else "",
        })
        rows.append(row)

    fieldnames = list(trials[0].keys()) + [
        "recall_pct", "retrace_samples", "retrace_len",
        "retrace_x_range", "retrace_z_range", "retrace_shape_r",
    ]
    trials_out = os.path.join(out_dir, "analysis_trials.csv")
    with open(trials_out, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fieldnames)
        w.writeheader()
        w.writerows(rows)

    conds = {}
    for r in rows:
        conds.setdefault(r.get("condition", ""), []).append(r)

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

    conds_out = os.path.join(out_dir, "analysis_conditions.csv")
    with open(conds_out, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(cond_rows[0].keys()) if cond_rows else [])
        w.writeheader()
        w.writerows(cond_rows)

    # Per-participant condition order: which condition each participant got, in the
    # order they got it (trials.csv already carries this via participant_id +
    # presentation_order + condition on every row -- this just makes it a ready-made
    # artifact instead of something reconstructed by hand each time).
    order_by_participant = {}
    for r in rows:
        pid = r.get("participant_id", "")
        order_by_participant.setdefault(pid, []).append(
            (to_int(r, "presentation_order"), r.get("condition", ""))
        )

    order_rows = []
    for pid, seq in sorted(order_by_participant.items()):
        seq.sort(key=lambda x: x[0])
        order_rows.append({
            "participant_id": pid,
            "n_trials": len(seq),
            "condition_order": " -> ".join(cond for _, cond in seq),
        })

    order_out = os.path.join(out_dir, "analysis_participant_order.csv")
    with open(order_out, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["participant_id", "n_trials", "condition_order"])
        w.writeheader()
        w.writerows(order_rows)

    n_part = len({t.get("participant_id") for t in trials})
    print(f"Parsed {n_part} participant(s), {len(rows)} trial(s).")
    print()
    print("Per-condition recall (mean % correct):")
    for c in cond_rows:
        print(f"  {c['condition']:16s} n={c['n_trials']:2d}  "
              f"recall={c['recall_pct_mean']:5.1f}% (sd {c['recall_pct_sd']:.1f})  "
              f"retrace_len={c['retrace_len_mean']:.2f}")
    print()
    print("Condition order per participant:")
    for o in order_rows:
        print(f"  {o['participant_id']:6s} ({o['n_trials']} trial(s)): {o['condition_order']}")
    print(f"\nWrote:\n  {trials_out}\n  {conds_out}\n  {order_out}")


if __name__ == "__main__":
    main()
