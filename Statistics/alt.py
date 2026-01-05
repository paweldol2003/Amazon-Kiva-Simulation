import os
import numpy as np
import pandas as pd
import matplotlib.pyplot as plt

CSV_PATH = r"AlgorithmResults.csv"
OUT_DIR = "plots_alt"

def ensure_out_dir():
    os.makedirs(OUT_DIR, exist_ok=True)

def read_results():
    df = pd.read_csv(CSV_PATH, sep=';')
    df["Success"] = df["Success"].astype(bool)
    df["TimeMs_num"] = df["TimeMs"].astype(str).str.replace(",", ".", regex=False).astype(float)
    df.loc[df["Manhattan"] == 0, "Manhattan"] = np.nan
    df["Stretch"] = df["PathLength"] / df["Manhattan"]
    return df

def savefig(name):
    plt.tight_layout()
    plt.savefig(os.path.join(OUT_DIR, name), dpi=200)
    plt.close()

# ---------- 1) Mean ± std ----------
def bar_mean_std(df, metric, title, filename):
    sdf = df[df["Success"]].copy()
    stats = sdf.groupby("Algorithm")[metric].agg(["mean", "std", "count"]).sort_values("mean")
    algos = stats.index.tolist()

    means = stats["mean"].values
    stds = stats["std"].values

    plt.figure()
    plt.bar(algos, means, yerr=stds, capsize=6)
    plt.ylabel(metric)
    plt.title(title + " (mean ± std, Success only)")
    savefig(filename)

# ---------- 2) ECDF ----------
def plot_ecdf(df, metric, title, filename):
    sdf = df[df["Success"]].copy()
    plt.figure()
    for algo, g in sdf.groupby("Algorithm"):
        x = np.sort(g[metric].dropna().values)
        if len(x) == 0:
            continue
        y = np.arange(1, len(x) + 1) / len(x)
        plt.step(x, y, where="post", label=algo)

    plt.xlabel(metric)
    plt.ylabel("ECDF")
    plt.title(title + " (Success only)")
    plt.legend()
    savefig(filename)

# ---------- 3) Histogram overlay ----------
def hist_overlay(df, metric, title, filename, bins=30):
    sdf = df[df["Success"]].copy()
    plt.figure()
    for algo, g in sdf.groupby("Algorithm"):
        x = g[metric].dropna().values
        if len(x) == 0:
            continue
        plt.hist(x, bins=bins, alpha=0.5, density=True, label=algo)
    plt.xlabel(metric)
    plt.ylabel("Density")
    plt.title(title + " (Success only)")
    plt.legend()
    savefig(filename)

# ---------- 4) Strip/jitter + mean ----------
def strip_with_mean(df, metric, title, filename, jitter=0.08):
    sdf = df[df["Success"]].copy()
    algos = sorted(sdf["Algorithm"].unique())
    x_pos = np.arange(len(algos))

    plt.figure()
    for i, algo in enumerate(algos):
        vals = sdf.loc[sdf["Algorithm"] == algo, metric].dropna().values
        if len(vals) == 0:
            continue
        # jitter w osi X
        x = i + np.random.uniform(-jitter, jitter, size=len(vals))
        plt.scatter(x, vals, s=10, alpha=0.5)

        # średnia jako pozioma kreska
        m = np.mean(vals)
        plt.plot([i - 0.2, i + 0.2], [m, m])

    plt.xticks(x_pos, algos)
    plt.ylabel(metric)
    plt.title(title + " (points + mean, Success only)")
    savefig(filename)

# ---------- 5) Radar (wielokryterialne podsumowanie) ----------
def radar_summary(df, filename="radar_summary.png"):
    # Metryki: SuccessRate (max), PathLength (min), Rotations (min), Stretch (min)
    # Normalizacja do [0,1] tak, żeby 1 = najlepsze.
    sdf = df.copy()
    algos = sorted(sdf["Algorithm"].unique())

    # agregaty (SuccessRate liczymy na wszystkich, reszta na Success only)
    success_rate = sdf.groupby("Algorithm")["Success"].mean()

    s_ok = sdf[sdf["Success"]].copy()
    path_mean = s_ok.groupby("Algorithm")["PathLength"].mean()
    rot_mean = s_ok.groupby("Algorithm")["Rotations"].mean()
    str_mean = s_ok.groupby("Algorithm")["Stretch"].mean()

    # pomocnicza normalizacja
    def norm_max(series):
        s = series.reindex(algos)
        mn, mx = s.min(), s.max()
        return (s - mn) / (mx - mn) if mx != mn else s*0 + 1.0

    def norm_min(series):
        # mniejsze lepsze -> odwracamy
        s = series.reindex(algos)
        mn, mx = s.min(), s.max()
        return (mx - s) / (mx - mn) if mx != mn else s*0 + 1.0

    v1 = norm_max(success_rate)          # im większy tym lepiej
    v2 = norm_min(path_mean)             # im mniejszy tym lepiej
    v3 = norm_min(rot_mean)
    v4 = norm_min(str_mean)

    labels = ["Success", "PathLength", "Rotations", "Stretch"]
    values = pd.DataFrame({
        "Success": v1,
        "PathLength": v2,
        "Rotations": v3,
        "Stretch": v4
    }, index=algos)

    # Radar
    angles = np.linspace(0, 2*np.pi, len(labels), endpoint=False).tolist()
    angles += angles[:1]

    plt.figure()
    ax = plt.subplot(111, polar=True)

    for algo in algos:
        data = values.loc[algo, labels].values.tolist()
        data += data[:1]
        ax.plot(angles, data, label=algo)
        ax.fill(angles, data, alpha=0.08)

    ax.set_xticks(angles[:-1])
    ax.set_xticklabels(labels)
    ax.set_yticks([0.25, 0.5, 0.75, 1.0])
    ax.set_ylim(0, 1.0)
    plt.title("Multi-metric summary (normalized, 1 = best)")
    plt.legend(loc="upper right", bbox_to_anchor=(1.25, 1.1))
    savefig(filename)

def main():
    ensure_out_dir()
    df = read_results()

    # Mean ± std
    bar_mean_std(df, "PathLength", "Path length", "01_bar_meanstd_pathlength.png")
    bar_mean_std(df, "Rotations", "Rotations", "02_bar_meanstd_rotations.png")
    bar_mean_std(df, "Stretch", "Stretch", "03_bar_meanstd_stretch.png")

    # ECDF
    plot_ecdf(df, "PathLength", "ECDF of PathLength", "04_ecdf_pathlength.png")
    plot_ecdf(df, "Rotations", "ECDF of Rotations", "05_ecdf_rotations.png")
    plot_ecdf(df, "Stretch", "ECDF of Stretch", "06_ecdf_stretch.png")

    # Histogram overlay
    hist_overlay(df, "PathLength", "Histogram of PathLength", "07_hist_pathlength.png", bins=25)

    # Strip/jitter + mean
    strip_with_mean(df, "PathLength", "PathLength distribution", "08_strip_pathlength.png")
    strip_with_mean(df, "Rotations", "Rotations distribution", "09_strip_rotations.png")

    # Radar summary
    radar_summary(df, filename="10_radar_summary.png")

    print("Gotowe. Pliki w:", os.path.abspath(OUT_DIR))

if __name__ == "__main__":
    main()
