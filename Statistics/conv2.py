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
# 2. KLASYFIKACJA ZAKRESÓW
# ============================

def classify_range(manh: float) -> str:
    """Classify trajectory by Manhattan length."""
    if manh <= 40:
        return 'krótkie trasy (M ≤ 40)'
    elif manh <= 80:
        return 'średnie trasy (41 ≤ M ≤ 80)'
    else:
        return 'długie trasy (M > 80)'

# ============================
# 3. TIME GRID & CURVES
# ============================

def build_time_grid(df_alg: pd.DataFrame,
                    dt: float = 10.0,
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
        curves[range]['opt'] -> DataFrame(TimeMs, OptRatio_mean)
        curves[range]['imp'] -> DataFrame(TimeMs, Imp_mean)
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

            opt_mean = np.nanmean(opt_arr, axis=0)
            imp_mean = np.nanmean(imp_arr, axis=0)

            df_opt = pd.DataFrame({
                'TimeMs': time_grid,
                'OptRatio_mean': opt_mean,
            })

            df_imp = pd.DataFrame({
                'TimeMs': time_grid,
                'Imp_mean': imp_mean,
            })
        else:
            df_opt = pd.DataFrame({'TimeMs': time_grid})
            df_imp = pd.DataFrame({'TimeMs': time_grid})

        curves[rlabel]['opt'] = df_opt
        curves[rlabel]['imp'] = df_imp

    return curves


# ============================
# 4. PER-RUN METRICS (CZAS + ITERACJE)
# ============================

def compute_run_metrics(
    df_alg: pd.DataFrame,
    alg_name: str,
    ks_rel: Tuple[float, ...] = (1.5, 1.2)
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
    - TimeFirstcel             – czas znalezienia pierwszej "drogi do celu"
    - TimeFirstOptimal          – czas pierwszego osiągnięcia Fitness == Fitness_final
    - TimeFirstcelNorm         – TimeFirstcel / Manhattan
    - TimeFirstOptimalNorm      – TimeFirstOptimal / Manhattan
    - Time_kX_Y                 – czas osiągnięcia jakości <= k * OptRatio_final (np. k=1.5 -> Time_k1_5)

    --- iteracje ---
    - IterFirstcel             – numer iteracji przy pierwszym "cel"
    - IterFirstOptimal          – numer iteracji przy pierwszym optimum
    - IterFirstcelNorm         – IterFirstcel / Manhattan
    - IterFirstOptimalNorm      – IterFirstOptimal / Manhattan
    - Iter_kX_Y                 – iteracja osiągnięcia jakości <= k * OptRatio_final
    """

    records = []

    for run_id, run in df_alg.groupby('RunId'):
        # sort po czasie
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

        # 1) Time/IterFirstcel – zależne od algorytmu
        idx_cel = None
        if alg_name.lower().startswith('firefly'):
            # FA: pierwsze Fitness > 40
            mask_cel = fitness_vals > 40
            if mask_cel.any():
                idx_cel = int(mask_cel.argmax())
        elif alg_name.lower().startswith('aco'):
            # ACO: pierwsze powtórzenie Fitness
            for i in range(1, len(fitness_vals)):
                if fitness_vals[i] == fitness_vals[i - 1]:
                    idx_cel = i
                    break
        elif alg_name.lower().startswith('cha') or alg_name.lower().startswith('camel'):
            # CHA: pierwsza iteracja
            idx_cel = 0

        if idx_cel is not None:
            time_first_cel = float(times[idx_cel])
            iter_first_cel = float(iters[idx_cel])
        else:
            time_first_cel = np.nan
            iter_first_cel = np.nan

        # 2) Time/IterFirstOptimal – pierwszy raz, gdy Fitness == Fitness_final
        mask_opt = fitness_vals == final_fit
        if mask_opt.any():
            idx_opt = int(mask_opt.argmax())
            time_first_opt = float(times[idx_opt])
            iter_first_opt = float(iters[idx_opt])
        else:
            time_first_opt = np.nan
            iter_first_opt = np.nan

        # 3) ImprovementCount – liczba spadków BestPathLength
        diff = np.diff(best_len)
        improvement_count = int(np.sum(diff < 0))

        # 4) Normalizacje przez Manhattan (czas + iteracje)
        time_first_cel_norm = time_first_cel / manh if not np.isnan(time_first_cel) else np.nan
        time_first_opt_norm = time_first_opt / manh if not np.isnan(time_first_opt) else np.nan

        iter_first_cel_norm = iter_first_cel / manh if not np.isnan(iter_first_cel) else np.nan
        iter_first_opt_norm = iter_first_opt / manh if not np.isnan(iter_first_opt) else np.nan

        # 5) Czasy i iteracje t_k (k * OptRatio_final)
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

        # 6) Zapis rekordu
        record = {
            'Algorithm': alg_name,
            'RunId': run_id,
            'Manhattan': manh,
            'ImprovementCount': improvement_count,
            'OptRatio_final': final_opt_ratio,

            'TimeFirstcel': time_first_cel,
            'TimeFirstOptimal': time_first_opt,
            'TimeFirstcelNorm': time_first_cel_norm,
            'TimeFirstOptimalNorm': time_first_opt_norm,

            'IterFirstcel': iter_first_cel,
            'IterFirstOptimal': iter_first_opt,
            'IterFirstcelNorm': iter_first_cel_norm,
            'IterFirstOptimalNorm': iter_first_opt_norm,
        }

        record.update(time_k_dict)
        record.update(iter_k_dict)

        records.append(record)

    return pd.DataFrame(records)


# ============================
# 5. NOWY TRÓJWYKRES:
#    ITERACJE VS CZAS DLA 4 METRYK
# ============================
from matplotlib.lines import Line2D  # możesz też dać ten import na górze pliku

from matplotlib.lines import Line2D  # zostaw jak było na górze pliku


def plot_mean_path_all_algorithms_with_markers(
    alg_curves: Dict[str, Dict[str, Dict[str, pd.DataFrame]]],
    alg_per_run: Dict[str, pd.DataFrame]
):
    """
    Trójwykres porównawczy:
    - wiersze: krótkie / średnie / długie trasy (pionowo)
    - na każdym panelu: 3 algorytmy naraz (ACO, FA, CHA)
      jako średnia długość trasy w czasie
      + punkty: cel, 25%, 50%, 75%, 95%, 99%, 100% drogi do optimum.

    Dla każdego algorytmu i zakresu:
    path_len_mean(t) = OptRatio_mean(t) * mean(Manhattan w tym zakresie)
    """

    ranges = ['krótkie trasy (M ≤ 40)', 'średnie trasy (41 ≤ M ≤ 80)', 'długie trasy (M > 80)']
    algs = list(alg_curves.keys())

    # markery dla punktów
    markers = {
        'cel': 'X',
        '25%': 'o',
        '50%': 's',
        '75%': '^',
        '95%': 'D',
        '99%': 'P',
        '100%': 'v'
    }

    # 3 wiersze, 1 kolumna – pionowo
    fig, axes = plt.subplots(3, 1, figsize=(7, 10), sharex=True, sharey=False)

    for ax, rlabel in zip(axes, ranges):
        has_any = False

        for alg_name in algs:
            curves_for_alg = alg_curves[alg_name]
            per_run = alg_per_run[alg_name]

            df_opt = curves_for_alg[rlabel]['opt']
            if 'OptRatio_mean' not in df_opt.columns:
                continue

            time_grid = df_opt['TimeMs'].values
            opt_series = df_opt['OptRatio_mean'].values

            valid_mask = ~np.isnan(opt_series)
            if not valid_mask.any():
                continue

            first_idx = np.argmax(valid_mask)
            last_idx = len(opt_series) - 1 - np.argmax(valid_mask[::-1])

            t_valid = time_grid[first_idx:last_idx + 1]
            opt_valid = opt_series[first_idx:last_idx + 1]

            # średni Manhattan w tej grupie dla tego algorytmu
            df_runs = per_run.copy()
            df_runs['Range'] = df_runs['Manhattan'].apply(classify_range)
            runs_in_range = df_runs[df_runs['Range'] == rlabel]
            if runs_in_range.empty:
                continue

            mean_manh = runs_in_range['Manhattan'].mean()
            path_len_mean = opt_valid * mean_manh

            # linia algorytmu
            line, = ax.plot(t_valid, path_len_mean, label=alg_name)
            color = line.get_color()
            has_any = True

            # --- punkt "cel" ---
            mean_cel_time = runs_in_range['TimeFirstcel'].mean()
            if not np.isnan(mean_cel_time):
                idx_cel = int(np.argmin(np.abs(t_valid - mean_cel_time)))
                ax.scatter(
                    t_valid[idx_cel],
                    path_len_mean[idx_cel],
                    marker=markers['cel'],
                    s=40,
                    color=color,
                    edgecolors='black',
                    zorder=5
                )

            # --- 25/50/75/95/99/100% postępu ---
            opt_start = opt_valid[0]
            opt_final = np.nanmin(opt_valid)

            mask_100 = np.isclose(opt_valid, opt_final, rtol=1e-4, atol=1e-6)
            if mask_100.any():
                idx_100 = int(mask_100.argmax())
            else:
                idx_100 = len(opt_valid) - 1

            if opt_start > opt_final:
                progress_levels = [
                    ('25%', 0.25),
                    ('50%', 0.50),
                    ('75%', 0.75),
                    ('95%', 0.95),
                    ('99%', 0.99),
                    ('100%', 1.00),
                ]

                for label, p in progress_levels:
                    if p < 1.0:
                        target_opt = opt_start - p * (opt_start - opt_final)
                        mask_p = opt_valid <= target_opt
                        if not mask_p.any():
                            continue
                        idx_p = int(mask_p.argmax())
                    else:
                        idx_p = idx_100

                    ax.scatter(
                        t_valid[idx_p],
                        path_len_mean[idx_p],
                        marker=markers[label],
                        s=35,
                        color=color,
                        edgecolors='black',
                        zorder=5
                    )

        ax.set_title(rlabel)
        ax.set_xlabel('Czas [ms]')
        ax.grid(alpha=0.3)

        if not has_any:
            ax.text(0.5, 0.5, 'brak danych', transform=ax.transAxes,
                    ha='center', va='center', fontsize=9, color='gray')

    axes[0].set_ylabel('Średnia długość trasy [kroki]')

    # --- legenda dla algorytmów (kolory linii) ---
    handles_alg, labels_alg = axes[0].get_legend_handles_labels()
    legend_alg = axes[0].legend(handles_alg, labels_alg, title='Algorytm',
                                fontsize=8, loc='upper right')
    axes[0].add_artist(legend_alg)

    # --- osobna legenda dla znaczników (kształty) ---
    marker_handles = [
        Line2D([0], [0], marker=markers['cel'], color='none',
               markerfacecolor='gray', markeredgecolor='black', label='cel'),
        Line2D([0], [0], marker=markers['25%'], color='none',
               markerfacecolor='gray', markeredgecolor='black', label='25%'),
        Line2D([0], [0], marker=markers['50%'], color='none',
               markerfacecolor='gray', markeredgecolor='black', label='50%'),
        Line2D([0], [0], marker=markers['75%'], color='none',
               markerfacecolor='gray', markeredgecolor='black', label='75%'),
        Line2D([0], [0], marker=markers['95%'], color='none',
               markerfacecolor='gray', markeredgecolor='black', label='95%'),
        Line2D([0], [0], marker=markers['99%'], color='none',
               markerfacecolor='gray', markeredgecolor='black', label='99%'),
        Line2D([0], [0], marker=markers['100%'], color='none',
               markerfacecolor='gray', markeredgecolor='black', label='100%'),
    ]
    axes[1].legend(marker_handles, [h.get_label() for h in marker_handles],
                   title='Punkty', fontsize=8, loc='upper right')

    fig.suptitle(
        'Porównanie algorytmów: średnia długość trasy w czasie\n'
        'z punktami celu oraz 25/50/75/95/99/100% drogi do optimum',
    )
    plt.tight_layout()
    plt.show()


# ============================
# 6. PRZYKŁADOWE UŻYCIE
# ============================

if __name__ == "__main__":
    # Ścieżki do plików
    paths = {
        'ACO': "ACOConvergenceLog.csv",
        'FA': "FAConvergenceLog.csv",
        'CHA': "CHAConvergenceLog.csv"
    }

    alg_data: Dict[str, pd.DataFrame] = {}
    alg_per_run: Dict[str, pd.DataFrame] = {}
    alg_curves: Dict[str, Dict[str, Dict[str, pd.DataFrame]]] = {}

    for alg_name, path in paths.items():
        df_alg = load_algorithm_log(path, alg_name)
        per_run = compute_run_metrics(df_alg, alg_name, ks_rel=(1.5, 1.2))
        curves = build_optratio_and_improvement_curves(df_alg, dt=50.0, max_time=1000.0)

        alg_data[alg_name] = df_alg
        alg_per_run[alg_name] = per_run
        alg_curves[alg_name] = curves

        # NOWY TRÓJWYKRES iteracje vs czas dla 4 metryk

        # ewentualnie: debug / podgląd
        # print(per_run.head())
    for alg_name in alg_curves.keys():
        curves_for_alg = alg_curves[alg_name]
        per_run = alg_per_run[alg_name]

    plot_mean_path_all_algorithms_with_markers(alg_curves, alg_per_run)


