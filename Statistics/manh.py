import os
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt

CSV_PATH = r"AlgorithmResults.csv"
OUT_DIR = "plots_manhattan"

# -----------------------------
# IO + preprocessing
# -----------------------------
def ensure_out_dir():
    os.makedirs(OUT_DIR, exist_ok=True)

def savefig(name: str):
    plt.tight_layout()
    plt.savefig(os.path.join(OUT_DIR, name), dpi=220)
    plt.close()

def read_results():
    df = pd.read_csv(CSV_PATH, sep=';')
    required = {"Algorithm","TimeMs","PathLength","Rotations","Success","Step","Manhattan"}
    missing = required - set(df.columns)
    if missing:
        raise ValueError(f"Missing columns: {missing}. Found: {list(df.columns)}")

    df["Success"] = df["Success"].astype(bool)

    # parse TimeMs (not used, but keep it consistent)
    df["TimeMs_num"] = df["TimeMs"].astype(str).str.replace(",", ".", regex=False).astype(float)

    # Manhattan=0 would break ratios
    df.loc[df["Manhattan"] == 0, "Manhattan"] = np.nan

    df["Stretch"] = df["PathLength"] / df["Manhattan"]
    df["ExcessPath"] = df["PathLength"] - df["Manhattan"]  # ile ponad heurystykę
    return df

def add_bins(df: pd.DataFrame, bin_width: int = 5):
    # Biny po Manhattan: [0-4], [5-9], ...
    mn = int(np.nanmin(df["Manhattan"]))
    mx = int(np.nanmax(df["Manhattan"]))
    start = (mn // bin_width) * bin_width
    end = ((mx // bin_width) + 1) * bin_width
    bins = np.arange(start, end + bin_width, bin_width)

    df["M_Bin"] = pd.cut(df["Manhattan"], bins=bins, right=False, include_lowest=True)

    # pomocnicze: środek binu jako liczba (do wykresów liniowych)
    centers = {}
    for b in df["M_Bin"].cat.categories:
        centers[b] = (b.left + b.right) / 2
    df["M_BinCenter"] = df["M_Bin"].map(centers).astype(float)

    return df

# -----------------------------
# Helpers for Manhattan-based trends
# -----------------------------
def binned_stat(sdf: pd.DataFrame, metric: str, stat: str = "mean"):
    # returns (x_centers, y_values) per algorithm
    out = {}
    for algo, g in sdf.groupby("Algorithm"):
        grp = g.dropna(subset=["M_BinCenter", metric]).groupby("M_BinCenter")[metric]
        if stat == "mean":
            y = grp.mean()
        elif stat == "median":
            y = grp.median()
        elif stat == "std":
            y = grp.std()
        elif stat == "count":
            y = grp.count()
        elif stat == "p25":
            y = grp.quantile(0.25)
        elif stat == "p75":
            y = grp.quantile(0.75)
        elif stat == "p10":
            y = grp.quantile(0.10)
        elif stat == "p90":
            y = grp.quantile(0.90)
        else:
            raise ValueError("Unknown stat")
        out[algo] = (y.index.values, y.values)
    return out

def plot_scatter_by_algo(sdf, x, y, title, filename, alpha=0.6, s=10, line_y_equals_x=False):
    plt.figure()
    for algo, g in sdf.groupby("Algorithm"):
        plt.scatter(g[x], g[y], s=s, alpha=alpha, label=algo)
    if line_y_equals_x:
        xmin = np.nanmin(sdf[x]); xmax = np.nanmax(sdf[x])
        xs = np.linspace(xmin, xmax, 200)
        plt.plot(xs, xs)
    plt.xlabel(x)
    plt.ylabel(y)
    plt.title(title)
    plt.legend()
    savefig(filename)

def plot_trend_lines(sdf, metric, stat, title, filename):
    trends = binned_stat(sdf, metric=metric, stat=stat)
    plt.figure()
    for algo, (xs, ys) in trends.items():
        plt.plot(xs, ys, marker='o', linewidth=1.5, label=algo)
    plt.xlabel("Manhattan (bin center)")
    plt.ylabel(f"{metric} ({stat})")
    plt.title(title)
    plt.legend()
    savefig(filename)

def plot_band_lines(sdf, metric, low_stat="p25", high_stat="p75", mid_stat="median", title="", filename=""):
    low = binned_stat(sdf, metric, low_stat)
    high = binned_stat(sdf, metric, high_stat)
    mid = binned_stat(sdf, metric, mid_stat)

    plt.figure()
    for algo in sorted(sdf["Algorithm"].unique()):
        if algo not in mid: 
            continue
        xs, m = mid[algo]
        _, lo = low.get(algo, (xs, np.full_like(xs, np.nan)))
        _, hi = high.get(algo, (xs, np.full_like(xs, np.nan)))

        plt.plot(xs, m, marker='o', linewidth=1.5, label=algo)
        # pasmo między kwantylami
        plt.fill_between(xs, lo, hi, alpha=0.12)

    plt.xlabel("Manhattan (bin center)")
    plt.ylabel(metric)
    plt.title(title)
    plt.legend()
    savefig(filename)

# -----------------------------
# Success rate vs Manhattan
# -----------------------------
def plot_success_rate_by_manhattan(df, filename=""):
    # overall success vs Manhattan bin
    g = df.groupby("M_Bin")["Success"].mean() * 100.0
    centers = [(b.left + b.right)/2 for b in g.index]
    plt.figure()
    plt.plot(centers, g.values, marker='o')
    plt.xlabel("Manhattan (bin center)")
    plt.ylabel("Success rate [%]")
    plt.title("Success rate vs Manhattan (all algorithms pooled)")
    savefig(filename)

def plot_success_rate_by_manhattan_per_algo(df, filename=""):
    plt.figure()
    for algo, g in df.groupby("Algorithm"):
        s = g.groupby("M_Bin")["Success"].mean() * 100.0
        centers = [(b.left + b.right)/2 for b in s.index]
        plt.plot(centers, s.values, marker='o', linewidth=1.5, label=algo)
    plt.xlabel("Manhattan (bin center)")
    plt.ylabel("Success rate [%]")
    plt.title("Success rate vs Manhattan (per algorithm)")
    plt.legend()
    savefig(filename)

# -----------------------------
# Win-rate by Manhattan bins
# -----------------------------
def win_rate_by_bin(df, metric: str, minimize: bool = True):
    """
    For each Step, pick winner by metric among algorithms (requires all algorithms present for that Step and all successes).
    Then aggregate winners per Manhattan bin (using that Step's Manhattan; assumes Manhattan identical per Step).
    Tie splits point.
    Returns DataFrame: index=bin_center, columns=algos, values=win_rate[%].
    """
    algos = sorted(df["Algorithm"].unique())

    # only successes
    w = df[df["Success"]].copy()

    # keep only steps with all algorithms present
    ok = w.groupby("Step")["Algorithm"].apply(lambda s: set(s) == set(algos))
    good_steps = ok[ok].index
    w = w[w["Step"].isin(good_steps)].copy()

    # map Step -> Manhattan bin center
    step_to_bin = w.groupby("Step")["M_BinCenter"].first()

    # collect points
    points = {}  # bin_center -> {algo: points, total_steps: n}
    for step, g in w.groupby("Step"):
        g2 = g[["Algorithm", metric]].dropna()
        if g2.empty:
            continue
        best = g2[metric].min() if minimize else g2[metric].max()
        winners = g2.loc[g2[metric] == best, "Algorithm"].tolist()
        if not winners:
            continue
        share = 1.0 / len(winners)
        b = float(step_to_bin.loc[step])

        if b not in points:
            points[b] = {"_steps": 0, **{a: 0.0 for a in algos}}
        points[b]["_steps"] += 1
        for win in winners:
            points[b][win] += share

    if not points:
        return pd.DataFrame()

    # to DataFrame
    rows = []
    for b in sorted(points.keys()):
        total = points[b]["_steps"]
        row = {"M_BinCenter": b}
        for a in algos:
            row[a] = (points[b][a] / total) * 100.0 if total > 0 else 0.0
        rows.append(row)

    out = pd.DataFrame(rows).set_index("M_BinCenter")
    return out

def plot_winrate_lines(df, metric, minimize, title, filename):
    wr = win_rate_by_bin(df, metric=metric, minimize=minimize)
    if wr.empty:
        print(f"[WARN] No win-rate data for {metric}")
        return
    plt.figure()
    for algo in wr.columns:
        plt.plot(wr.index.values, wr[algo].values, marker='o', linewidth=1.5, label=algo)
    plt.xlabel("Manhattan (bin center)")
    plt.ylabel("Win rate [% of Steps in bin]")
    plt.title(title)
    plt.legend()
    savefig(filename)

# -----------------------------
# Heatmap: algorithm vs Manhattan bin
# -----------------------------
def plot_heatmap_mean_metric_success(df, metric, title, filename):
    sdf = df[df["Success"]].copy()
    pivot = sdf.pivot_table(index="Algorithm", columns="M_BinCenter", values=metric, aggfunc="mean")
    # ensure stable order
    pivot = pivot.reindex(index=sorted(pivot.index))
    cols = sorted(pivot.columns)
    pivot = pivot[cols]

    plt.figure()
    plt.imshow(pivot.values, aspect="auto")
    plt.xticks(ticks=np.arange(len(cols)), labels=[int(c) for c in cols], rotation=45)
    plt.yticks(ticks=np.arange(len(pivot.index)), labels=pivot.index.tolist())
    plt.xlabel("Manhattan (bin center)")
    plt.ylabel("Algorithm")
    plt.title(title + " (mean, Success only)")
    plt.colorbar()
    savefig(filename)

# -----------------------------
# Main: generate many Manhattan-based plots
# -----------------------------
def main(bin_width=5):
    ensure_out_dir()
    df = read_results()
    df = add_bins(df, bin_width=bin_width)

    # Use Success-only for quality plots to avoid mixing fails
    sdf = df[df["Success"]].copy()

    # 1) Scatter: PathLength vs Manhattan (+ y=x)
    plot_scatter_by_algo(
        sdf, x="Manhattan", y="PathLength",
        title="PathLength vs Manhattan (Success only)",
        filename="01_scatter_path_vs_manhattan.png",
        line_y_equals_x=True
    )

    # 2) Scatter: Rotations vs Manhattan
    plot_scatter_by_algo(
        sdf, x="Manhattan", y="Rotations",
        title="Rotations vs Manhattan (Success only)",
        filename="02_scatter_rotations_vs_manhattan.png"
    )

    # 3) Scatter: Stretch vs Manhattan
    plot_scatter_by_algo(
        sdf, x="Manhattan", y="Stretch",
        title="Stretch (=PathLength/Manhattan) vs Manhattan (Success only)",
        filename="03_scatter_stretch_vs_manhattan.png"
    )

    # 4) Scatter: ExcessPath vs Manhattan
    plot_scatter_by_algo(
        sdf, x="Manhattan", y="ExcessPath",
        title="ExcessPath (=PathLength-Manhattan) vs Manhattan (Success only)",
        filename="04_scatter_excess_vs_manhattan.png"
    )

    # 5-8) Trend lines (mean/median) vs Manhattan bins
    plot_trend_lines(sdf, "PathLength", "mean",   "Mean PathLength vs Manhattan bins",   "05_line_mean_path_vs_bins.png")
    plot_trend_lines(sdf, "PathLength", "median", "Median PathLength vs Manhattan bins", "06_line_median_path_vs_bins.png")
    plot_trend_lines(sdf, "Rotations",  "mean",   "Mean Rotations vs Manhattan bins",    "07_line_mean_rot_vs_bins.png")
    plot_trend_lines(sdf, "Stretch",    "mean",   "Mean Stretch vs Manhattan bins",      "08_line_mean_stretch_vs_bins.png")

    # 9) Quantile band (median + IQR) for PathLength vs bins
    plot_band_lines(
        sdf, metric="PathLength",
        low_stat="p25", high_stat="p75", mid_stat="median",
        title="PathLength vs Manhattan bins (median + IQR band, Success only)",
        filename="09_band_path_iqr.png"
    )

    # 10) Quantile band for Stretch vs bins
    plot_band_lines(
        sdf, metric="Stretch",
        low_stat="p25", high_stat="p75", mid_stat="median",
        title="Stretch vs Manhattan bins (median + IQR band, Success only)",
        filename="10_band_stretch_iqr.png"
    )

    # 11-12) Success rate vs Manhattan (overall + per algo)
    plot_success_rate_by_manhattan(df, filename="11_success_rate_vs_manhattan_overall.png")
    plot_success_rate_by_manhattan_per_algo(df, filename="12_success_rate_vs_manhattan_per_algo.png")

    # 13-14) Win-rate by Manhattan bins (who wins steps)
    plot_winrate_lines(
        df, metric="PathLength", minimize=True,
        title="Win-rate vs Manhattan bins (best = shortest PathLength, Success-only Steps)",
        filename="13_winrate_pathlength_vs_bins.png"
    )
    plot_winrate_lines(
        df, metric="Rotations", minimize=True,
        title="Win-rate vs Manhattan bins (best = fewest Rotations, Success-only Steps)",
        filename="14_winrate_rotations_vs_bins.png"
    )

    # 15-17) Heatmaps: algorithm vs Manhattan bin (mean metrics)
    plot_heatmap_mean_metric_success(df, "PathLength", "Heatmap: PathLength", "15_heatmap_mean_pathlength.png")
    plot_heatmap_mean_metric_success(df, "Rotations",  "Heatmap: Rotations",  "16_heatmap_mean_rotations.png")
    plot_heatmap_mean_metric_success(df, "Stretch",    "Heatmap: Stretch",    "17_heatmap_mean_stretch.png")

    # 18) Density of Manhattan itself (żeby pokazać, gdzie masz dane)
    plt.figure()
    plt.hist(df["Manhattan"].dropna().values, bins=30, density=False)
    plt.xlabel("Manhattan")
    plt.ylabel("Count")
    plt.title("Distribution of Manhattan (all rows)")
    savefig("18_hist_manhattan.png")

    print("Done. Output folder:", os.path.abspath(OUT_DIR))

if __name__ == "__main__":
    # możesz zmienić szerokość binów, np. 3 / 5 / 10
    main(bin_width=5)
