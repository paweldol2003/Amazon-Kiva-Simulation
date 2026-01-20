import pandas as pd
import numpy as np
import matplotlib.pyplot as plt
from typing import Dict, Tuple


# ============================
# 1. LOADING & PREPROCESSING
# ============================

def load_algorithm_log(path: str, alg_name: str) -> pd.DataFrame:
    """
    Load convergence log for a single algorithm.
    Expected columns:
    Algorithm;Iteration;TimeMs;Manhattan;Fitness;BestPathLength
    """
    df = pd.read_csv(path, sep=';', decimal=',')
    # Enforce algorithm name (in case file contains only one algorithm)
    df['Algorithm'] = alg_name

    # Ensure numeric types
    numeric_cols = ['Iteration', 'TimeMs', 'Manhattan', 'Fitness', 'BestPathLength']
    for col in numeric_cols:
        df[col] = pd.to_numeric(df[col], errors='coerce')

    df = df.dropna(subset=['TimeMs', 'Manhattan', 'Fitness', 'BestPathLength'])

    # Detect runs: whenever time goes backwards -> new run
    df = df.sort_index()  # keep original order
    df['RunId'] = (df['TimeMs'].diff() < 0).cumsum()

    return df


# ============================
# 2. PER-RUN METRICS
# ============================

def summarize_time_metrics(per_run: pd.DataFrame, timeout_ms: float = 1000.0) -> pd.DataFrame:
    """
    Zbiorcze statystyki po algorytmie:
    - średni czas do celu / optimum
    - znormalizowane czasy
    - odsetek przebiegów, które zdążyły przed timeoutem
    - średnie t_k, jeśli są obecne
    """

    # wykryj kolumny Time_k...
    tk_cols = [c for c in per_run.columns if c.startswith('Time_k')]

    rows = []
    for alg, grp in per_run.groupby('Algorithm'):
        row = {
            'Algorithm': alg,
            'mean_TimeFirstGoal': grp['TimeFirstGoal'].mean(),
            'mean_TimeFirstOptimal': grp['TimeFirstOptimal'].mean(),
            'mean_TimeFirstGoalNorm': grp['TimeFirstGoalNorm'].mean(),
            'mean_TimeFirstOptimalNorm': grp['TimeFirstOptimalNorm'].mean(),
            'SuccessRate_opt_le_timeout': (grp['TimeFirstOptimal'] <= timeout_ms).mean()
        }
        for col in tk_cols:
            row[f'mean_{col}'] = grp[col].mean()

        rows.append(row)

    return pd.DataFrame(rows)

def compute_run_metrics(
    df_alg: pd.DataFrame,
    alg_name: str,
    ks_rel: Tuple[float, ...] = (1.5, 1.2, 1.1)
) -> pd.DataFrame:
    """
    Compute per-run metrics dla danego algorytmu.

    Wejście: df_alg z kolumnami:
        ['Algorithm', 'RunId', 'Iteration', 'TimeMs', 'Manhattan', 'Fitness', 'BestPathLength']

    Zwracane metryki (kolumny w per_run):

    --- podstawowe ---
    - Algorithm
    - RunId
    - Manhattan
    - ImprovementCount          – ile razy BestPathLength się poprawił (spadek)
    - OptRatio_final            – BestPathLength_final / Manhattan

    --- czasy (ms) ---
    - TimeFirstGoal             – czas znalezienia pierwszej "drogi do celu"
    - TimeFirstOptimal          – czas pierwszego osiągnięcia Fitness == Fitness_final
    - TimeFirstGoalNorm         – TimeFirstGoal / Manhattan
    - TimeFirstOptimalNorm      – TimeFirstOptimal / Manhattan
    - Time_kX_Y                 – czas osiągnięcia jakości <= k * OptRatio_final (np. k=1.5 -> Time_k1_5)

    --- iteracje ---
    - IterFirstGoal             – numer iteracji przy pierwszym "goal"
    - IterFirstOptimal          – numer iteracji przy pierwszym optimum
    - IterFirstGoalNorm         – IterFirstGoal / Manhattan
    - IterFirstOptimalNorm      – IterFirstOptimal / Manhattan
    - Iter_kX_Y                 – iteracja osiągnięcia jakości <= k * OptRatio_final
    """

    records = []

    for run_id, run in df_alg.groupby('RunId'):
        # sort po czasie (na wszelki wypadek)
        run = run.sort_values('TimeMs').reset_index(drop=True)

        manh = run['Manhattan'].iloc[0]
        times = run['TimeMs'].values
        iters = run['Iteration'].values
        fitness_vals = run['Fitness'].values
        best_len = run['BestPathLength'].values

        # OptRatio(t) i optimum
        opt_ratio_series = best_len / manh
        final_best_len = best_len[-1]
        final_opt_ratio = final_best_len / manh
        final_fit = fitness_vals[-1]

        # ======================
        # 1) Time/IterFirstGoal – zależne od algorytmu
        # ======================
        idx_goal = None

        if alg_name.lower().startswith('firefly'):
            # FA: pierwsze Fitness > 40
            mask_goal = fitness_vals > 40
            if mask_goal.any():
                idx_goal = int(mask_goal.argmax())

        elif alg_name.lower().startswith('aco'):
            # ACO: pierwsze powtórzenie Fitness
            for i in range(1, len(fitness_vals)):
                if fitness_vals[i] == fitness_vals[i - 1]:
                    idx_goal = i
                    break

        elif alg_name.lower().startswith('cha') or alg_name.lower().startswith('camel'):
            # CHA: pierwsza iteracja
            idx_goal = 0

        # wyciągamy czas/iterację jeśli istnieje
        if idx_goal is not None:
            time_first_goal = float(times[idx_goal])
            iter_first_goal = float(iters[idx_goal])
        else:
            time_first_goal = np.nan
            iter_first_goal = np.nan

        # ======================
        # 2) Time/IterFirstOptimal
        # ======================
        mask_opt = fitness_vals == final_fit
        if mask_opt.any():
            idx_opt = int(mask_opt.argmax())
            time_first_opt = float(times[idx_opt])
            iter_first_opt = float(iters[idx_opt])
        else:
            time_first_opt = np.nan
            iter_first_opt = np.nan

        # ======================
        # 3) ImprovementCount
        # ======================
        diff = np.diff(best_len)
        improvement_count = int(np.sum(diff < 0))

        # ======================
        # 4) Normalizacje przez Manhattan
        # ======================
        time_first_goal_norm = time_first_goal / manh if not np.isnan(time_first_goal) else np.nan
        time_first_opt_norm = time_first_opt / manh if not np.isnan(time_first_opt) else np.nan

        iter_first_goal_norm = iter_first_goal / manh if not np.isnan(iter_first_goal) else np.nan
        iter_first_opt_norm = iter_first_opt / manh if not np.isnan(iter_first_opt) else np.nan

        # ======================
        # 5) Czasy i iteracje t_k (k * OptRatio_final)
        # ======================
        time_k_dict = {}
        iter_k_dict = {}

        for k_rel in ks_rel:
            thresh = final_opt_ratio * k_rel
            mask_k = opt_ratio_series <= thresh
            if mask_k.any():
                idx_k = int(mask_k.argmax())
                t_k = float(times[idx_k])
                it_k = float(iters[idx_k])
            else:
                t_k = np.nan
                it_k = np.nan

            suffix = str(k_rel).replace('.', '_')  # "1.5" -> "1_5"
            time_k_dict[f'Time_k{suffix}'] = t_k
            iter_k_dict[f'Iter_k{suffix}'] = it_k

        # ======================
        # 6) Zapis rekordu
        # ======================
        record = {
            'Algorithm': alg_name,
            'RunId': run_id,
            'Manhattan': manh,
            'ImprovementCount': improvement_count,
            'OptRatio_final': final_opt_ratio,

            'TimeFirstGoal': time_first_goal,
            'TimeFirstOptimal': time_first_opt,
            'TimeFirstGoalNorm': time_first_goal_norm,
            'TimeFirstOptimalNorm': time_first_opt_norm,

            'IterFirstGoal': iter_first_goal,
            'IterFirstOptimal': iter_first_opt,
            'IterFirstGoalNorm': iter_first_goal_norm,
            'IterFirstOptimalNorm': iter_first_opt_norm,
        }

        record.update(time_k_dict)
        record.update(iter_k_dict)

        records.append(record)

    return pd.DataFrame(records)


# ============================
# 3. TIME GRID & GROUPING BY RANGE
# ============================

def classify_range(manh: float) -> str:
    """Classify trajectory by Manhattan length."""
    if manh <= 40:
        return 'krótkie trasy (M ≤ 40)'
    elif manh <= 80:
        return 'średnie trasy (41 ≤ M ≤ 80)'
    else:
        return 'długie trasy (M > 80)'

def build_time_grid(df_alg: pd.DataFrame,
                    dt: float = 50.0,
                    max_time: float = 1000.0) -> np.ndarray:
    """Build a common time grid in ms."""
    t_max = min(max_time, df_alg['TimeMs'].max())
    grid = np.arange(0.0, t_max + 0.1, dt)
    return grid


def build_optratio_and_improvement_curves(
    df_alg: pd.DataFrame,
    dt: float = 50.0,
    max_time: float = 1000.0
) -> Dict[str, Dict[str, pd.DataFrame]]:
    """
    For a given algorithm:
    - build time grid
    - for each RunId:
        * OptRatio(t) = BestPathLength(t)/Manhattan (ffill on grid)
        * Improvement%(t) relative to first known OptRatio
    - group by Manhattan range (short/medium/long)
    Return:
        curves[range]['opt'] -> DataFrame(time, mean_opt, median_opt)
        curves[range]['imp'] -> DataFrame(time, mean_imp, median_imp)
    """

    time_grid = build_time_grid(df_alg, dt=dt, max_time=max_time)
    ranges = ['krótkie trasy (M ≤ 40)', 'średnie trasy (41 ≤ M ≤ 80)', 'długie trasy (M > 80)']

    opt_curves = {r: [] for r in ranges}
    imp_curves = {r: [] for r in ranges}

    for run_id, run in df_alg.groupby('RunId'):
        run = run.sort_values('TimeMs').reset_index(drop=True)
        manh = run['Manhattan'].iloc[0]
        rlabel = classify_range(manh)

        # OptRatio(t)
        opt_ratio = run['BestPathLength'] / manh
        series = opt_ratio.copy()
        series.index = run['TimeMs']

        # Reindex on common time grid with forward-fill
        series_resampled = series.reindex(time_grid, method='ffill')

        # Improvement%(t) relative to first known value
        first_valid = series_resampled.dropna().iloc[0] if series_resampled.notna().any() else np.nan
        if np.isnan(first_valid):
            continue
        improvement = (first_valid - series_resampled) / first_valid * 100.0

        opt_curves[rlabel].append(series_resampled.values)
        imp_curves[rlabel].append(improvement.values)

    curves = {}
    for rlabel in ranges:
        curves[rlabel] = {}
        if len(opt_curves[rlabel]) > 0:
            opt_arr = np.vstack(opt_curves[rlabel])
            imp_arr = np.vstack(imp_curves[rlabel])

            # nanmean/median over runs
            opt_mean = np.nanmean(opt_arr, axis=0)
            opt_median = np.nanmedian(opt_arr, axis=0)
            imp_mean = np.nanmean(imp_arr, axis=0)
            imp_median = np.nanmedian(imp_arr, axis=0)

            df_opt = pd.DataFrame({
                'TimeMs': time_grid,
                'OptRatio_mean': opt_mean,
                'OptRatio_median': opt_median
            })

            df_imp = pd.DataFrame({
                'TimeMs': time_grid,
                'Imp_mean': imp_mean,
                'Imp_median': imp_median
            })
        else:
            df_opt = pd.DataFrame({'TimeMs': time_grid})
            df_imp = pd.DataFrame({'TimeMs': time_grid})

        curves[rlabel]['opt'] = df_opt
        curves[rlabel]['imp'] = df_imp

    return curves

# ============================
# 5. ZBIORCZE FIGURY (algorytmy jeden pod drugim)
# ============================
def plot_optratio_time_by_range_all(alg_curves: Dict[str, Dict[str, Dict[str, pd.DataFrame]]]):
    ranges = ['krótkie trasy (M ≤ 40)', 'średnie trasy (41 ≤ M ≤ 80)', 'długie trasy (M > 80)']
    algs = list(alg_curves.keys())

    default_colors = ['tab:blue', 'tab:orange', 'tab:green', 'tab:red', 'tab:purple']
    alg_colors = {alg: default_colors[i % len(default_colors)] for i, alg in enumerate(algs)}

    # JEDYNA FIGURA: 3 zakresy jeden pod drugim
    fig, axes = plt.subplots(3, 1, figsize=(7, 10), sharex=True, sharey=True)

    for j, rlabel in enumerate(ranges):
        ax = axes[j]
        for alg in algs:
            df_opt = alg_curves[alg][rlabel]['opt']
            if 'OptRatio_mean' in df_opt.columns:
                ax.plot(df_opt['TimeMs'], df_opt['OptRatio_mean'], label=alg, color=alg_colors[alg])
        ax.set_title(rlabel)
        ax.set_xlabel('Czas [ms]')
        ax.grid(alpha=0.3)
        if j == 1:
            ax.set_ylabel('Ścieżka / odległość Manhattan')
        if j == 0:
            ax.legend(title='Algorytm')

    fig.suptitle('Zbieżność współczynnika nadmiarowości ścieżki')
    plt.tight_layout(rect=[0, 0.02, 1, 0.95])
    plt.show()
def plot_improvementpct_time_by_range_all(alg_curves: Dict[str, Dict[str, Dict[str, pd.DataFrame]]]):
    ranges = ['krótkie trasy (M ≤ 40)', 'średnie trasy (41 ≤ M ≤ 80)', 'długie trasy (M > 80)']
    algs = list(alg_curves.keys())

    default_colors = ['tab:blue', 'tab:orange', 'tab:green', 'tab:red', 'tab:purple']
    alg_colors = {alg: default_colors[i % len(default_colors)] for i, alg in enumerate(algs)}

    # JEDYNA FIGURA: 3 zakresy jeden pod drugim
    fig, axes = plt.subplots(3, 1, figsize=(7, 10), sharex=True, sharey=True)

    for j, rlabel in enumerate(ranges):
        ax = axes[j]
        for alg in algs:
            df_imp = alg_curves[alg][rlabel]['imp']
            if 'Imp_mean' in df_imp.columns:
                ax.plot(df_imp['TimeMs'], df_imp['Imp_mean'], label=alg, color=alg_colors[alg])
        ax.set_title(rlabel)
        ax.set_xlabel('Czas [ms]')
        ax.grid(alpha=0.3)
        if j == 1:
            ax.set_ylabel('Poprawa [%]')
        if j == 0:
            ax.legend(title='Algorytm')

    fig.suptitle('Procentowa poprawa jakości')
    plt.tight_layout(rect=[0, 0.02, 1, 0.95])
    plt.show()

# 6. PRZYKŁADOWE UŻYCIE
# ============================

if __name__ == "__main__":
    # Ścieżki do plików
    paths = {
        'ACO': "ACOConvergenceLog.csv",
        'FA': "FAConvergenceLog.csv",
        'CHA': "CHAConvergenceLog.csv"
    }

    alg_data = {}
    alg_per_run = {}
    alg_curves = {}

    for alg_name, path in paths.items():
        df_alg = load_algorithm_log(path, alg_name)
        per_run = compute_run_metrics(df_alg, alg_name, ks_rel=(1.8, 1.5, 1.1))
        curves = build_optratio_and_improvement_curves(df_alg, dt=50.0, max_time=1000.0)

        alg_data[alg_name] = df_alg
        alg_per_run[alg_name] = per_run
        alg_curves[alg_name] = curves

    # Wykresy zbiorcze (algorytmy jeden pod drugim, zakresy obok siebie)
    plot_optratio_time_by_range_all(alg_curves)
    plot_improvementpct_time_by_range_all(alg_curves)