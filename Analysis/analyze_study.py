#!/usr/bin/env python3
"""
Embodied-Data-VR -- study analysis pipeline (M16, updated for CSV logging; M34
refactored so the GUI in analysis_gui.py can reuse the same pipeline).

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
    python analyze_study.py <study_data_dir> --participant p03   # just one participant

For a point-and-click version of this (pick a folder, see the participant list,
Analyze All / Analyze one at a time) see analysis_gui.py in this same folder.

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
                         (see notes), AND (M31) a PNG plot is generated per trial
                         under <out_dir>/plots/ comparing the original data's value
                         profile against what the participant actually retraced --
                         answers "what did their retrace look like as a graph, next
                         to the real one". Requires matplotlib; skipped with a note
                         if it isn't installed (`pip install matplotlib`), or pass
                         --no-plots to skip on purpose. Defaults to datasets.json
                         sitting next to this script, if present.
    --no-plots           skip generating the per-trial retrace-vs-original PNGs even
                         when matplotlib and --datasets are both available.
    --participant        only analyze this one participant (e.g. "p03") instead of
                         every participant folder under study_data_dir.

Outputs (written to --out, default <study_data_dir>/analysis so they never collide
with Unity's own raw trials.csv sitting alongside them; a --participant run defaults
to <study_data_dir>/analysis/<participant> instead so per-participant re-runs don't
overwrite the all-participants output):
    analysis_trials.csv      one row per trial: every trials.csv column plus the
                              derived retrace metrics (and recall_pct).
    analysis_conditions.csv  per-condition aggregate (means across all trials).
    analysis_participant_order.csv  which condition each participant got, in order.
    summary.csv              the same headline numbers printed to stdout, as a
                              one-column CSV of report lines, so the "result" of a
                              run is a plain-text-readable file and not just a
                              terminal printout.
    plots/*.png               (M31, when --datasets is available) original vs.
                              retraced value profile per trial.

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

try:
    import matplotlib
    matplotlib.use("Agg")   # headless: just save PNG files, no display needed
    import matplotlib.pyplot as plt
    HAVE_MPL = True
except ImportError:
    HAVE_MPL = False

DEFAULT_DATASETS_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "datasets.json")


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


def list_participants(study_data_dir):
    """
    Participant folder names directly under study_data_dir (e.g. ["p01", "p02"]),
    sorted, skipping this script's own "analysis" output folder and dotfiles. This
    is what analysis_gui.py shows in its participant list after you point it at a
    study data folder -- it mirrors the per-participant layout SessionController
    itself writes (see StudyDataSummary() in SessionController.cs).
    """
    if not os.path.isdir(study_data_dir):
        return []
    names = []
    for entry in sorted(os.listdir(study_data_dir)):
        full = os.path.join(study_data_dir, entry)
        if os.path.isdir(full) and entry != "analysis" and not entry.startswith("."):
            names.append(entry)
    return names


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


def save_retrace_plot(path, samples, values, corr, participant_id, order, condition, dataset_id):
    """
    Saves a PNG comparing the original dataset's value profile against what the
    participant actually retraced (M31) -- both resampled to the same number of
    points and each normalized 0..1 independently, same basis as retrace_shape_corr,
    so the plotted shapes and the printed correlation number agree. Returns True if a
    plot was written, False if there wasn't enough retrace data to make one (fewer
    than 2 usable samples).
    """
    if not HAVE_MPL:
        return False
    k = len(values)
    if k < 2:
        return False
    lat = resample_lateral(samples, k)
    if lat is None:
        return False

    orig_norm = normalize(values)
    retrace_norm = normalize(lat)
    xs = list(range(k))
    corr_label = f"{corr:.2f}" if not math.isnan(corr) else "n/a"

    fig, ax = plt.subplots(figsize=(6, 3.5))
    ax.plot(xs, orig_norm, color="#3b6fd6", linewidth=2, label="Original data")
    ax.plot(xs, retrace_norm, color="#e0663c", linewidth=2, linestyle="--", label="Participant retrace")
    ax.set_xlabel("Point index along the walk")
    ax.set_ylabel("Normalized value (0-1)")
    ax.set_ylim(-0.05, 1.05)
    ax.set_title(
        f"{participant_id}  trial {order}  {condition}  ({dataset_id})\n"
        f"shape correlation r = {corr_label}",
        fontsize=9,
    )
    ax.legend(fontsize=8, loc="upper right")
    fig.tight_layout()
    fig.savefig(path, dpi=140)
    plt.close(fig)
    return True


def to_int(row, key, default=0):
    try:
        return int(row.get(key, default) or default)
    except (TypeError, ValueError):
        return default


def run_analysis(study_data_dir, out_dir=None, datasets_path=None, make_plots=True,
                  participant_id=None, log=print):
    """
    Runs the full analysis pipeline: reads trials.csv/retracing_log.csv (across
    every participant subfolder, or just `participant_id` when given), derives
    per-trial retrace metrics, writes analysis_trials.csv / analysis_conditions.csv
    / analysis_participant_order.csv / summary.csv (+ plots/*.png when a datasets
    file is available), and returns a dict describing what happened. Both main()
    (the CLI, all participants) and analysis_gui.py (all participants, or one at a
    time via `participant_id`) call this -- it's the single place the analysis
    actually happens so the two stay in sync.

    `log` is called once per line of the human-readable summary (default: print);
    analysis_gui.py passes something that appends to its on-screen status box
    instead.
    """
    if out_dir is None:
        out_dir = os.path.join(study_data_dir, "analysis", participant_id) if participant_id \
            else os.path.join(study_data_dir, "analysis")
    os.makedirs(out_dir, exist_ok=True)

    if datasets_path is None and os.path.exists(DEFAULT_DATASETS_PATH):
        datasets_path = DEFAULT_DATASETS_PATH
    datasets = {}
    if datasets_path and os.path.exists(datasets_path):
        with open(datasets_path) as f:
            datasets = json.load(f)

    trials = read_all_rows(study_data_dir, "trials.csv")
    if participant_id:
        trials = [t for t in trials if t.get("participant_id") == participant_id]
    if not trials:
        msg = (f"No trials.csv rows found under {study_data_dir}"
               + (f" for participant {participant_id}" if participant_id else
                  " (checked the flat layout and every participant subfolder)"))
        log(msg)
        return {"ok": False, "message": msg}

    retrace_rows = read_all_rows(study_data_dir, "retracing_log.csv")
    if participant_id:
        retrace_rows = [r for r in retrace_rows if r.get("participant_id") == participant_id]

    retrace_by_trial = {}
    for r in retrace_rows:
        retrace_by_trial.setdefault(trial_key(r), []).append(r)

    do_plots = HAVE_MPL and bool(datasets) and make_plots
    plots_dir = os.path.join(out_dir, "plots")
    if do_plots:
        os.makedirs(plots_dir, exist_ok=True)
    n_plots = 0

    rows = []
    for t in trials:
        samples = retrace_by_trial.get(trial_key(t), [])
        pm = path_metrics(samples)
        ds = t.get("dataset_id", "")
        corr = float("nan")
        if ds in datasets:
            corr = retrace_shape_corr(samples, datasets[ds])
            if do_plots:
                pid = t.get("participant_id", "p??")
                order = to_int(t, "presentation_order")
                cond = t.get("condition", "")
                fname = f"{pid}_t{order:02d}_{cond}.png"
                plot_path = os.path.join(plots_dir, fname)
                if save_retrace_plot(plot_path, samples, datasets[ds], corr, pid, order, cond, ds):
                    n_plots += 1

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

    summary_lines = []
    summary_lines.append(f"Parsed {n_part} participant(s), {len(rows)} trial(s).")
    summary_lines.append("")
    summary_lines.append("Per-condition recall (mean % correct):")
    for c in cond_rows:
        summary_lines.append(
            f"  {c['condition']:16s} n={c['n_trials']:2d}  "
            f"recall={c['recall_pct_mean']:5.1f}% (sd {c['recall_pct_sd']:.1f})  "
            f"retrace_len={c['retrace_len_mean']:.2f}"
        )
    summary_lines.append("")
    summary_lines.append("Condition order per participant:")
    for o in order_rows:
        summary_lines.append(f"  {o['participant_id']:6s} ({o['n_trials']} trial(s)): {o['condition_order']}")

    wrote_lines = [trials_out, conds_out, order_out]
    if do_plots:
        wrote_lines.append(f"{plots_dir}/  ({n_plots} retrace-vs-original PNG(s))")
    elif not datasets:
        summary_lines.append("")
        summary_lines.append("(No retrace-vs-original plots: no datasets.json found -- "
                              "pass --datasets datasets.json to enable them.)")
    elif not HAVE_MPL:
        summary_lines.append("")
        summary_lines.append("(No retrace-vs-original plots: matplotlib isn't installed "
                              "-- `pip install matplotlib`.)")

    summary_out = os.path.join(out_dir, "summary.csv")
    with open(summary_out, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["info"])
        for line in summary_lines:
            w.writerow([line])
    wrote_lines.append(summary_out)

    for line in summary_lines:
        log(line)
    log("")
    log("Wrote:\n  " + "\n  ".join(wrote_lines))

    return {
        "ok": True,
        "out_dir": out_dir,
        "trials_csv": trials_out,
        "conditions_csv": conds_out,
        "order_csv": order_out,
        "summary_csv": summary_out,
        "plots_dir": plots_dir if do_plots else None,
        "n_participants": n_part,
        "n_trials": len(rows),
        "n_plots": n_plots,
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("study_data_dir")
    ap.add_argument("--out", default=None)
    ap.add_argument("--datasets", default=None,
                     help="Defaults to datasets.json next to this script, if present.")
    ap.add_argument("--no-plots", action="store_true",
                     help="Skip generating per-trial retrace-vs-original PNGs.")
    ap.add_argument("--participant", default=None,
                     help="Only analyze this one participant (e.g. p03) instead of everyone.")
    args = ap.parse_args()

    run_analysis(
        args.study_data_dir,
        out_dir=args.out,
        datasets_path=args.datasets,
        make_plots=not args.no_plots,
        participant_id=args.participant,
    )


if __name__ == "__main__":
    main()
