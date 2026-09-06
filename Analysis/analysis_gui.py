#!/usr/bin/env python3
"""
Embodied-Data-VR -- study analysis GUI (M34).

A point-and-click front end for analyze_study.py, for when you'd rather not use
the command line: pick your study data folder, see every participant it finds,
and either analyze all of them at once or one at a time. Each run writes the
same files analyze_study.py always has (analysis_trials.csv,
analysis_conditions.csv, analysis_participant_order.csv, summary.csv, and
plots/*.png when datasets.json is available) into an output folder -- by
default <study data folder>/analysis for "Analyze All", and
<study data folder>/analysis/<participant> for a single participant, so the
two never overwrite each other. Everything runs on the standard library plus
analyze_study.py itself (matplotlib is optional -- used for the PNG plots when
installed, skipped with a note otherwise).

Run it with:
    python analysis_gui.py
(or double-click it, if .py files are associated with Python on this machine)
"""
import os
import queue
import subprocess
import sys
import threading
import tkinter as tk
from tkinter import filedialog, messagebox, scrolledtext, ttk

import analyze_study


class AnalysisGUI(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Embodied-Data-VR -- Study Analysis")
        self.geometry("760x560")
        self.minsize(620, 440)

        self.study_dir = tk.StringVar(value="")
        self.out_dir = tk.StringVar(value="")
        self.status_var = tk.StringVar(value="Choose a study data folder to begin.")
        self._log_queue = queue.Queue()
        self._worker_running = False
        self._last_out_dir = None

        self._build_widgets()
        self.after(100, self._drain_log_queue)

    # ------------------------------------------------------------------ UI
    def _build_widgets(self):
        pad = {"padx": 8, "pady": 4}

        top = ttk.Frame(self)
        top.pack(fill="x", **pad)

        ttk.Label(top, text="Study data folder:").grid(row=0, column=0, sticky="w")
        ttk.Entry(top, textvariable=self.study_dir).grid(row=0, column=1, sticky="ew", padx=4)
        ttk.Button(top, text="Choose Folder...", command=self._choose_study_dir).grid(row=0, column=2)

        ttk.Label(top, text="Output folder:").grid(row=1, column=0, sticky="w")
        ttk.Entry(top, textvariable=self.out_dir).grid(row=1, column=1, sticky="ew", padx=4)
        ttk.Button(top, text="Choose Output...", command=self._choose_out_dir).grid(row=1, column=2)
        ttk.Label(top, text="(leave blank to use <study folder>/analysis)",
                  foreground="#666").grid(row=2, column=1, sticky="w", padx=4)

        top.columnconfigure(1, weight=1)

        mid = ttk.Frame(self)
        mid.pack(fill="both", expand=False, **pad)

        left = ttk.Frame(mid)
        left.pack(side="left", fill="y")
        ttk.Label(left, text="Participants found:").pack(anchor="w")
        list_frame = ttk.Frame(left)
        list_frame.pack(fill="y", expand=True)
        self.participant_list = tk.Listbox(list_frame, height=10, width=18, exportselection=False)
        self.participant_list.pack(side="left", fill="y")
        scrollbar = ttk.Scrollbar(list_frame, orient="vertical", command=self.participant_list.yview)
        scrollbar.pack(side="left", fill="y")
        self.participant_list.config(yscrollcommand=scrollbar.set)
        ttk.Button(left, text="Refresh List", command=self._refresh_participants).pack(fill="x", pady=(4, 0))

        right = ttk.Frame(mid)
        right.pack(side="left", fill="both", expand=True, padx=(16, 0))
        ttk.Label(right, text="Actions:").pack(anchor="w")
        self.analyze_all_btn = ttk.Button(right, text="Analyze All", command=self._on_analyze_all)
        self.analyze_all_btn.pack(fill="x", pady=2)
        self.analyze_selected_btn = ttk.Button(right, text="Analyze Selected Participant",
                                                command=self._on_analyze_selected)
        self.analyze_selected_btn.pack(fill="x", pady=2)
        self.open_output_btn = ttk.Button(right, text="Open Last Output Folder",
                                           command=self._open_last_output, state="disabled")
        self.open_output_btn.pack(fill="x", pady=(12, 2))

        mpl_note = "matplotlib found -- plots will be generated." if analyze_study.HAVE_MPL \
            else "matplotlib not installed -- plots will be skipped (pip install matplotlib)."
        ttk.Label(right, text=mpl_note, foreground="#666", wraplength=260,
                  justify="left").pack(anchor="w", pady=(12, 0))
        ds_note = ("Using datasets.json next to analyze_study.py for shape-correlation plots."
                   if os.path.exists(analyze_study.DEFAULT_DATASETS_PATH)
                   else "No datasets.json found next to analyze_study.py -- shape-correlation "
                        "plots will be skipped.")
        ttk.Label(right, text=ds_note, foreground="#666", wraplength=260,
                  justify="left").pack(anchor="w", pady=(4, 0))

        bottom = ttk.Frame(self)
        bottom.pack(fill="both", expand=True, **pad)
        ttk.Label(bottom, text="Log:").pack(anchor="w")
        self.log_box = scrolledtext.ScrolledText(bottom, height=14, state="disabled", wrap="word")
        self.log_box.pack(fill="both", expand=True)

        status_bar = ttk.Label(self, textvariable=self.status_var, relief="sunken", anchor="w")
        status_bar.pack(fill="x", side="bottom")

    # ------------------------------------------------------------- actions
    def _choose_study_dir(self):
        path = filedialog.askdirectory(title="Choose the study data folder (contains p01, p02, ...)")
        if not path:
            return
        self.study_dir.set(path)
        self._refresh_participants()

    def _choose_out_dir(self):
        path = filedialog.askdirectory(title="Choose an output folder")
        if path:
            self.out_dir.set(path)

    def _refresh_participants(self):
        self.participant_list.delete(0, "end")
        study_dir = self.study_dir.get().strip()
        if not study_dir:
            self.status_var.set("Choose a study data folder to begin.")
            return
        if not os.path.isdir(study_dir):
            self.status_var.set(f"Not a folder: {study_dir}")
            return
        participants = analyze_study.list_participants(study_dir)
        for p in participants:
            self.participant_list.insert("end", p)
        if participants:
            self.status_var.set(f"Found {len(participants)} participant(s) in {study_dir}")
        else:
            self.status_var.set(f"No participant subfolders found in {study_dir} "
                                 f"(expected p01, p02, ... each with trials.csv)")

    def _on_analyze_all(self):
        study_dir = self.study_dir.get().strip()
        if not self._require_study_dir(study_dir):
            return
        self._run_analysis_async(study_dir, participant_id=None, label="all participants")

    def _on_analyze_selected(self):
        study_dir = self.study_dir.get().strip()
        if not self._require_study_dir(study_dir):
            return
        sel = self.participant_list.curselection()
        if not sel:
            messagebox.showinfo("Pick a participant", "Select a participant in the list first.")
            return
        pid = self.participant_list.get(sel[0])
        self._run_analysis_async(study_dir, participant_id=pid, label=pid)

    def _require_study_dir(self, study_dir):
        if not study_dir or not os.path.isdir(study_dir):
            messagebox.showerror("No study data folder", "Choose a valid study data folder first.")
            return False
        return True

    def _run_analysis_async(self, study_dir, participant_id, label):
        if self._worker_running:
            messagebox.showinfo("Busy", "An analysis is already running -- wait for it to finish.")
            return
        self._worker_running = True
        self._set_buttons_enabled(False)
        self._clear_log()
        self._log(f"Analyzing {label} ...")
        self.status_var.set(f"Analyzing {label} ...")

        out_dir = self.out_dir.get().strip() or None

        def work():
            try:
                result = analyze_study.run_analysis(
                    study_dir,
                    out_dir=(os.path.join(out_dir, participant_id) if (out_dir and participant_id) else out_dir),
                    participant_id=participant_id,
                    log=lambda line: self._log_queue.put(("line", line)),
                )
                self._log_queue.put(("done", result))
            except Exception as exc:  # surfaced in the log box + a dialog, never a silent crash
                self._log_queue.put(("error", str(exc)))

        threading.Thread(target=work, daemon=True).start()

    def _open_last_output(self):
        if not self._last_out_dir or not os.path.isdir(self._last_out_dir):
            return
        if sys.platform.startswith("win"):
            os.startfile(self._last_out_dir)  # noqa: this GUI only ships/runs on Windows
        elif sys.platform == "darwin":
            subprocess.run(["open", self._last_out_dir])
        else:
            subprocess.run(["xdg-open", self._last_out_dir])

    # ------------------------------------------------------------ logging
    def _drain_log_queue(self):
        try:
            while True:
                kind, payload = self._log_queue.get_nowait()
                if kind == "line":
                    self._log(payload)
                elif kind == "done":
                    self._on_analysis_done(payload)
                elif kind == "error":
                    self._log(f"ERROR: {payload}")
                    self._worker_running = False
                    self._set_buttons_enabled(True)
                    self.status_var.set("Analysis failed -- see log.")
                    messagebox.showerror("Analysis failed", payload)
        except queue.Empty:
            pass
        self.after(100, self._drain_log_queue)

    def _on_analysis_done(self, result):
        self._worker_running = False
        self._set_buttons_enabled(True)
        if result.get("ok"):
            self._last_out_dir = result["out_dir"]
            self.open_output_btn.config(state="normal")
            self.status_var.set(
                f"Done -- {result['n_participants']} participant(s), {result['n_trials']} trial(s). "
                f"Saved to {result['out_dir']}"
            )
        else:
            self.status_var.set(result.get("message", "No data found."))

    def _log(self, text):
        self.log_box.config(state="normal")
        self.log_box.insert("end", text + "\n")
        self.log_box.see("end")
        self.log_box.config(state="disabled")

    def _clear_log(self):
        self.log_box.config(state="normal")
        self.log_box.delete("1.0", "end")
        self.log_box.config(state="disabled")

    def _set_buttons_enabled(self, enabled):
        state = "normal" if enabled else "disabled"
        self.analyze_all_btn.config(state=state)
        self.analyze_selected_btn.config(state=state)


if __name__ == "__main__":
    app = AnalysisGUI()
    app.mainloop()
