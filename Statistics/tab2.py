import pandas as pd
import numpy as np
from pathlib import Path
from typing import Dict

# ============================
# 1. CONFIG
# ============================

# Ścieżki do plików z logami konwergencji
BASE_DIR = Path(".")  # zmień jeśli trzeba

CONVERGENCE_FILES: Dict[str, Path] = {
    "ACO": BASE_DIR / "ACOConvergenceLog.csv",
    "FA": BASE_DIR / "FAConvergenceLog.csv",
    "CHA": BASE_DIR / "CHAConvergenceLog.csv",
}

# Opcjonalny 4. plik z dodatkowymi statystykami (jeśli go masz)
# np. coś w stylu "GlobalResults.csv" z kolumną "Algorithm"
FOURTH_FILE = BASE_DIR / "ExtraStats.csv"  # zmień nazwę lub zakomentuj jeśli nie używasz


# ============================
# 2. LOADING & PREPROCESSING
# ============================

def load_convergence_log(path: Path, alg_name: str) -> pd.DataFrame:
    """
    Load convergence log for a single algorithm.
    Expected columns:
    Algorithm;Iteration;TimeMs;Manhattan;Fitness;BestPathLength
    Separator: ';'
    Decimal: ','
    """
    df = pd.read_csv(path, sep=";", decimal=",")
    # Enforce algorithm name (in case file has no Algorithm column or wrong one)
    df["Algorithm"] = alg_name

    # Ensure numeric types
    numeric_cols = ["Iteration", "TimeMs", "Manhattan", "Fitness", "BestPathLength"]
    for col in numeric_cols:
        if col in df.columns:
            df[col] = pd.to_numeric(df[col], errors="coerce")

    # Drop rows with missing core fields
    df = df.dropna(subset=["TimeMs", "Fitness", "BestPathLength"])

    # Keep original order, then detect runs
    df = df.reset_index(drop=True)

    # RunId: whenever time goes backwards (TimeMs diff < 0) → new run
    time_diff = df["TimeMs"].diff()
    new_run_flags = (time_diff < 0) | time_diff.isna()
    df["RunId"] = new_run_flags.cumsum()  # 0, 1, 2, ...

    return df


# ============================
# 3. RUN-LEVEL SUMMARY
# ============================

def summarize_runs(df: pd.DataFrame) -> pd.DataFrame:
    """
    Create per-run summary:
      - convergence time (last TimeMs in run)
      - final best path length (last BestPathLength)
      - final fitness (last Fitness)
      - number of iterations in run
    Returns DataFrame with one row per run.
    """
    # Last row per RunId
    last_rows = df.sort_values(["RunId", "Iteration"]).groupby("RunId").tail(1)

    # Basic summary per run
    run_summary = pd.DataFrame({
        "Algorithm": last_rows["Algorithm"].values,
        "RunId": last_rows["RunId"].values,
        "ConvergenceTimeMs": last_rows["TimeMs"].values,
        "FinalBestPathLength": last_rows["BestPathLength"].values,
        "FinalFitness": last_rows["Fitness"].values,
    })

    # Number of iterations per run
    iters_per_run = df.groupby("RunId")["Iteration"].max().rename("NumIterations")
    run_summary = run_summary.merge(iters_per_run, left_on="RunId", right_index=True)

    return run_summary


# ============================
# 4. ALGORITHM-LEVEL COMPARISON
# ============================

def aggregate_algorithm_stats(run_summary: pd.DataFrame) -> pd.DataFrame:
    """
    Build algorithm-level comparison table from run-level data.
    For each algorithm compute mean, std, min, max for:
      - ConvergenceTimeMs
      - FinalBestPathLength
      - FinalFitness
      - NumIterations
    """
    agg = run_summary.groupby("Algorithm").agg(
        Runs=("RunId", "nunique"),

        # Convergence time
        ConvergenceTimeMs_mean=("ConvergenceTimeMs", "mean"),
        ConvergenceTimeMs_std=("ConvergenceTimeMs", "std"),
        ConvergenceTimeMs_min=("ConvergenceTimeMs", "min"),
        ConvergenceTimeMs_max=("ConvergenceTimeMs", "max"),

        # Final best path length
        FinalBestPathLength_mean=("FinalBestPathLength", "mean"),
        FinalBestPathLength_std=("FinalBestPathLength", "std"),
        FinalBestPathLength_min=("FinalBestPathLength", "min"),
        FinalBestPathLength_max=("FinalBestPathLength", "max"),

        # Final fitness
        FinalFitness_mean=("FinalFitness", "mean"),
        FinalFitness_std=("FinalFitness", "std"),
        FinalFitness_min=("FinalFitness", "min"),
        FinalFitness_max=("FinalFitness", "max"),

        # Number of iterations
        NumIterations_mean=("NumIterations", "mean"),
        NumIterations_std=("NumIterations", "std"),
        NumIterations_min=("NumIterations", "min"),
        NumIterations_max=("NumIterations", "max"),
    )

    # Optional: round for nicer output
    agg = agg.round(3)
    return agg


# ============================
# 5. OPTIONAL: MERGE 4TH CSV
# ============================

def load_and_merge_extra_stats(comparison_df: pd.DataFrame, extra_path: Path) -> pd.DataFrame:
    """
    Load additional CSV with some stats and merge it into comparison table.
    Assumes it has a column 'Algorithm' that matches index of comparison_df.
    """
    if not extra_path.exists():
        print(f"[INFO] Extra stats file not found: {extra_path}")
        return comparison_df

    extra = pd.read_csv(extra_path, sep=";", decimal=",")
    if "Algorithm" not in extra.columns:
        raise ValueError("Extra stats CSV must contain 'Algorithm' column.")

    # Set index as Algorithm for merge
    extra = extra.set_index("Algorithm")

    # Merge with comparison table (outer join to keep all algs)
    merged = comparison_df.merge(extra, left_index=True, right_index=True, how="left")
    return merged


# ============================
# 6. MAIN PIPELINE
# ============================

def build_comparison_table() -> pd.DataFrame:
    # 1. Wczytanie i scalanie per-run
    all_runs = []

    for alg_name, path in CONVERGENCE_FILES.items():
        print(f"[INFO] Loading {alg_name} from {path}")
        df = load_convergence_log(path, alg_name)
        rs = summarize_runs(df)
        all_runs.append(rs)

    all_runs_df = pd.concat(all_runs, ignore_index=True)

    # 2. Agregacja per-algorytm
    comparison_df = aggregate_algorithm_stats(all_runs_df)

    # 3. Ewentualne dołączenie 4. pliku
    if FOURTH_FILE is not None:
        comparison_df = load_and_merge_extra_stats(comparison_df, FOURTH_FILE)

    return comparison_df


if __name__ == "__main__":
    comparison = build_comparison_table()

    # Wyświetl w konsoli
    print("\n=== COMPARISON TABLE (per algorithm) ===")
    print(comparison)

    # Zapis do CSV
    comparison.to_csv("AlgorithmsComparisonTable.csv", sep=";")

    # Zapis do LaTeX (np. do wklejenia w pracę)
    latex_table = comparison.to_latex(
        buf=None,
        index=True,
        caption="Porównanie algorytmów ACO, FA oraz CHA na podstawie logów konwergencji.",
        label="tab:alg_comparison_convergence",
        float_format="%.3f"
    )
    with open("AlgorithmsComparisonTable.tex", "w", encoding="utf-8") as f:
        f.write(latex_table)

    print("\n[INFO] Saved AlgorithmsComparisonTable.csv and AlgorithmsComparisonTable.tex")
