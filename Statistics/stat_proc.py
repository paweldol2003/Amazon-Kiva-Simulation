import os
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt


CSV_PATH = r"AlgorithmResults.csv"   # <-- zmień jeśli trzeba
OUT_DIR = "plots"


def ensure_out_dir(path: str) -> None:
    os.makedirs(path, exist_ok=True)


def read_results(csv_path: str) -> pd.DataFrame:
    # CSV ma separator ';'
    df = pd.read_csv(csv_path, sep=';')

    required = {"Algorithm", "TimeMs", "PathLength", "Rotations", "Success", "Step", "Manhattan"}
    missing = required - set(df.columns)
    if missing:
        raise ValueError(f"Brakuje kolumn: {missing}. Mam: {list(df.columns)}")

    # Success bywa już bool, ale dopnijmy
    df["Success"] = df["Success"].astype(bool)

    # TimeMs ma przecinek dziesiętny (np. 1007,79) — parsujemy na float (nie używamy w wykresach)
    df["TimeMs_num"] = df["TimeMs"].astype(str).str.replace(",", ".", regex=False).astype(float)

    # Dla bezpieczeństwa: Manhattan nie powinien być 0, bo dzielimy
    df.loc[df["Manhattan"] == 0, "Manhattan"] = np.nan

    # Współczynnik "rozciągnięcia" ścieżki względem heurystyki
    df["Stretch"] = df["PathLength"] / df["Manhattan"]

    return df


def savefig(name: str) -> None:
    plt.tight_layout()
    plt.savefig(os.path.join(OUT_DIR, name), dpi=200)
    plt.close()


def plot_success_rate(df: pd.DataFrame) -> None:
    # % sukcesów per algorytm
    rate = df.groupby("Algorithm")["Success"].mean().sort_values(ascending=False) * 100.0

    plt.figure()
    plt.bar(rate.index, rate.values)
    plt.ylabel("Success rate [%]")
    plt.title("Success rate per algorithm")
    savefig("01_success_rate.png")


def boxplot_metric_success(df: pd.DataFrame, metric: str, title: str, filename: str) -> None:
    sdf = df[df["Success"]].copy()
    algos = sorted(sdf["Algorithm"].unique())
    data = [sdf.loc[sdf["Algorithm"] == a, metric].dropna().values for a in algos]

    plt.figure()
    plt.boxplot(data, labels=algos, showfliers=True)
    plt.ylabel(metric)
    plt.title(title)
    savefig(filename)


def scatter_path_vs_manhattan(df: pd.DataFrame) -> None:
    sdf = df[df["Success"]].copy()
    plt.figure()
    for a, g in sdf.groupby("Algorithm"):
        plt.scatter(g["Manhattan"], g["PathLength"], s=10, alpha=0.6, label=a)

    # linia odniesienia y=x (idealnie PathLength == Manhattan w pustej mapie bez przeszkód)
    x = np.linspace(sdf["Manhattan"].min(), sdf["Manhattan"].max(), 200)
    plt.plot(x, x)

    plt.xlabel("Manhattan distance")
    plt.ylabel("PathLength")
    plt.title("PathLength vs Manhattan (Success only)")
    plt.legend()
    savefig("04_scatter_path_vs_manhattan.png")


def win_rate_per_step(
    df: pd.DataFrame,
    metric: str,
    minimize: bool,
    require_all_algorithms: bool = True,
    require_all_success: bool = True
) -> pd.Series:
    """
    Win-rate: dla każdego Step wybieramy zwycięzcę (min lub max metric).
    Remisy dzielą punkt (np. 2 zwycięzców -> każdy dostaje 0.5).
    """
    all_algos = sorted(df["Algorithm"].unique())

    # Filtr na sukcesy (opcjonalnie)
    work = df.copy()
    if require_all_success:
        work = work[work["Success"]].copy()

    # Tylko kroki, gdzie występują wszystkie algorytmy (opcjonalnie)
    if require_all_algorithms:
        steps_ok = work.groupby("Step")["Algorithm"].apply(lambda s: set(s) == set(all_algos))
        good_steps = steps_ok[steps_ok].index
        work = work[work["Step"].isin(good_steps)].copy()

    # Sumujemy "punkty wygranych"
    wins = {a: 0.0 for a in all_algos}
    step_groups = work.groupby("Step")

    for step, g in step_groups:
        # wybór najlepszego (min/max)
        vals = g[[ "Algorithm", metric ]].dropna()
        if vals.empty:
            continue

        best_val = vals[metric].min() if minimize else vals[metric].max()
        winners = vals.loc[vals[metric] == best_val, "Algorithm"].tolist()
        if not winners:
            continue

        share = 1.0 / len(winners)
        for w in winners:
            wins[w] += share

    total_steps = len(step_groups)
    if total_steps == 0:
        return pd.Series(wins)

    # win-rate w %
    return (pd.Series(wins) / total_steps * 100.0).sort_values(ascending=False)


def plot_win_rate(df: pd.DataFrame, metric: str, minimize: bool, filename: str, title: str) -> None:
    wr = win_rate_per_step(df, metric=metric, minimize=minimize,
                           require_all_algorithms=True, require_all_success=True)

    plt.figure()
    plt.bar(wr.index, wr.values)
    plt.ylabel("Win rate [% of steps]")
    plt.title(title)
    savefig(filename)


def main():
    ensure_out_dir(OUT_DIR)
    df = read_results(CSV_PATH)

    # 1) Success rate
    plot_success_rate(df)

    # 2) Jakość ścieżki (tylko sukcesy)
    boxplot_metric_success(
        df, metric="PathLength",
        title="PathLength distribution (Success only)",
        filename="02_boxplot_pathlength_success.png"
    )

    # 3) Rotations (tylko sukcesy)
    boxplot_metric_success(
        df, metric="Rotations",
        title="Rotations distribution (Success only)",
        filename="03_boxplot_rotations_success.png"
    )

    # 4) PathLength vs Manhattan
    scatter_path_vs_manhattan(df)

    # 5) Stretch = PathLength / Manhattan (tylko sukcesy)
    boxplot_metric_success(
        df, metric="Stretch",
        title="Stretch = PathLength / Manhattan (Success only)",
        filename="05_boxplot_stretch_success.png"
    )

    # 6) Win-rate (kto wygrywa scenariusze)
    plot_win_rate(
        df, metric="PathLength", minimize=True,
        filename="06_winrate_pathlength.png",
        title="Win rate per Step (best = shortest PathLength, Success only)"
    )

    plot_win_rate(
        df, metric="Rotations", minimize=True,
        filename="07_winrate_rotations.png",
        title="Win rate per Step (best = fewest Rotations, Success only)"
    )

    print(f"Gotowe. Wykresy zapisane w folderze: {os.path.abspath(OUT_DIR)}")


if __name__ == "__main__":
    main()
