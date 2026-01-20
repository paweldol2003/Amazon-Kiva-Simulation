import pandas as pd
import numpy as np
from pathlib import Path

# ============================================
# 1. KONFIGURACJA
# ============================================

DATA_PATH = Path("dane.csv")  # zmień jeśli plik jest gdzie indziej

# ============================================
# 2. POMOCNICZE FUNKCJE DO WYSZUKIWANIA KOLUMN
# ============================================

def find_metric_column(df: pd.DataFrame) -> str:
    """
    Szuka kolumny odpowiadającej długości ścieżki.
    Normalizuje nazwy (małe litery, bez spacji i podkreśleń)
    i próbuje dopasować coś w stylu 'pathlength'.
    """
    target = "pathlength"
    for col in df.columns:
        norm = col.lower().replace(" ", "").replace("_", "")
        if target in norm:
            return col
    raise ValueError(
        "Nie znalazłem kolumny z długością ścieżki. "
        "Upewnij się, że masz np. 'PathLength' / 'path length'."
    )


def find_algorithm_column(df: pd.DataFrame) -> str:
    """
    Szuka kolumny z nazwą algorytmu:
    próbuje 'algorithm', 'alg', 'algorytm' po normalizacji.
    """
    candidates = ["algorithm", "alg", "algorytm"]
    for col in df.columns:
        norm = col.lower().replace(" ", "").replace("_", "")
        if norm in candidates:
            return col
    raise ValueError(
        "Nie znalazłem kolumny z nazwą algorytmu. "
        "Dodaj kolumnę 'Algorithm' albo podobną."
    )

# ============================================
# 3. WCZYTANIE DANYCH
# ============================================

def load_data(path: Path) -> pd.DataFrame:
    """
    Próbuje wczytać CSV z separatorem ';' i przecinkiem jako separator dziesiętny.
    Jeśli się wywali, próbuje standardowego wczytania.
    """
    try:
        df = pd.read_csv(path, sep=";", decimal=",")
    except Exception:
        df = pd.read_csv(path)
    return df

# ============================================
# 4. BUDOWANIE INSTANCJI (GRUP)
# ============================================

def assign_instances(df: pd.DataFrame, alg_col: str) -> pd.DataFrame:
    """
    Zakładamy, że dane są w formacie:
      kolejne N wierszy (N = liczba algorytmów) = ta sama instancja.
    Nadajemy kolumnę 'InstanceId' i wyrzucamy instancje, które
    nie mają kompletnego zestawu algorytmów.
    """
    df = df.reset_index(drop=True)
    n_algs = df[alg_col].nunique()
    if n_algs < 2:
        raise ValueError("Mam mniej niż 2 różne algorytmy w danych.")

    # przypisujemy instancje „pakietami” po n_algs wierszy
    df["InstanceId"] = df.index // n_algs

    # wyrzucamy instancje, w których nie ma wszystkich algorytmów
    counts = df.groupby("InstanceId")[alg_col].nunique()
    valid_ids = counts[counts == n_algs].index
    dropped = counts[counts != n_algs]
    if not dropped.empty:
        print("[WARN] Usuwam instancje bez pełnego zestawu algorytmów:", list(dropped.index))

    df = df[df["InstanceId"].isin(valid_ids)].copy()
    df["InstanceId"] = df["InstanceId"].astype(int)
    return df

# ============================================
# 5. STATYSTYKI OPISOWE
# ============================================

def compute_basic_stats(df: pd.DataFrame, alg_col: str, metric: str) -> pd.DataFrame:
    """
    Statystyki opisowe długości ścieżki:
      count, mean, std, median, Q1, Q3, min, max.
    """
    grouped = df.groupby(alg_col)[metric]
    basic = grouped.agg(
        count="count",
        mean="mean",
        std="std",
        median="median",
        q25=lambda x: x.quantile(0.25),
        q75=lambda x: x.quantile(0.75),
        min="min",
        max="max",
    )
    basic = basic.round(3)
    return basic

# ============================================
# 6. WIN RATE I ŚREDNI RANKING
# ============================================

def compute_win_rate_and_ranks(df: pd.DataFrame, alg_col: str, metric: str):
    """
    Dla każdej instancji:
      - liczy zwycięzcę (najmniejszy metric),
        przy remisach dzieli punkt na liczbę zwycięzców,
      - liczy ranking algorytmów (1 = najlepszy, rank 'average' przy remisach).
    Zwraca:
      win_table (DataFrame) i rank_table (DataFrame).
    """
    wins = {alg: 0.0 for alg in df[alg_col].unique()}
    rank_rows = []

    instances = df["InstanceId"].unique()
    n_instances = len(instances)

    for inst_id, sub in df.groupby("InstanceId"):
        # WIN RATE
        best_val = sub[metric].min()
        eps = 1e-9
        winners = sub[sub[metric] <= best_val + eps][alg_col].values
        if len(winners) > 0:
            score = 1.0 / len(winners)
            for w in winners:
                wins[w] += score

        # RANKI
        sub_rank = sub[[alg_col, metric]].copy()
        sub_rank["rank"] = sub_rank[metric].rank(method="average", ascending=True)
        rank_rows.append(sub_rank[[alg_col, "rank"]])

    # WIN TABLE
    win_rate = {alg: wins[alg] / n_instances for alg in wins.keys()}
    win_table = pd.DataFrame(
        {
            "WinCountWeighted": wins,
            "WinRate": win_rate,
        }
    )
    win_table = win_table.sort_values("WinRate", ascending=False)
    win_table = win_table.round(3)

    # RANK TABLE
    ranks_df = pd.concat(rank_rows, ignore_index=True)
    rank_table = (
        ranks_df.groupby(alg_col)["rank"]
        .agg(mean_rank="mean", std_rank="std")
        .round(3)
    )

    return win_table, rank_table

# ============================================
# 7. MACIERZ DOMINACJI
# ============================================

def compute_dominance_matrix(df: pd.DataFrame, alg_col: str, metric: str):
    """
    Macierz dominacji:
      dom_counts[A, B] = ile razy A miał mniejszy metric niż B (per instancja).
    Tylko jeśli oba algorytmy występują w danej instancji.
    Zwraca:
      dom_counts, dom_percent (oba DataFrame).
    """
    algs = sorted(df[alg_col].unique())
    dom_counts = pd.DataFrame(0, index=algs, columns=algs, dtype=int)

    n_instances = df["InstanceId"].nunique()

    for inst_id, sub in df.groupby("InstanceId"):
        # tylko algorytmy obecne w tej instancji
        present_algs = sub[alg_col].unique()
        values = sub.set_index(alg_col)[metric].to_dict()

        for a in present_algs:
            for b in present_algs:
                if a == b:
                    continue
                va = values[a]
                vb = values[b]
                if va < vb:
                    dom_counts.loc[a, b] += 1

    dom_percent = dom_counts / n_instances
    dom_percent = dom_percent.round(3)

    return dom_counts, dom_percent

# ============================================
# 8. GŁÓWNA FUNKCJA
# ============================================

def main():
    # 1) Wczytanie danych
    df = load_data(DATA_PATH)
    print("[INFO] Wczytane kolumny:", list(df.columns))

    # 2) Znalezienie kolumn
    metric_col = find_metric_column(df)
    alg_col = find_algorithm_column(df)
    print(f"[INFO] Używam kolumny metryki: {metric_col}")
    print(f"[INFO] Kolumna algorytmu: {alg_col}")

    # 3) Przypisanie instancji + wyrzucenie niekompletnych
    df = assign_instances(df, alg_col)
    n_instances = df["InstanceId"].nunique()
    print(f"[INFO] Liczba instancji (po filtracji): {n_instances}")
    print(f"[INFO] Algorytmy: {df[alg_col].unique()}")

    # 4) Statystyki opisowe
    basic_stats = compute_basic_stats(df, alg_col, metric_col)
    print("\n=== STATYSTYKI OPISOWE (PathLength) ===")
    print(basic_stats)

    # 5) Win rate + rank
    win_table, rank_table = compute_win_rate_and_ranks(df, alg_col, metric_col)
    print("\n=== WIN RATE (na instancjach) ===")
    print(win_table)
    print("\n=== ŚREDNI RANKING (1 = najlepszy) ===")
    print(rank_table)

    # 6) Macierz dominacji
    dom_counts, dom_percent = compute_dominance_matrix(df, alg_col, metric_col)
    print("\n=== MACIERZ DOMINACJI (liczba instancji, A lepszy od B) ===")
    print(dom_counts)
    print("\n=== MACIERZ DOMINACJI (procent instancji, A lepszy od B) ===")
    print(dom_percent)

    # 7) ZAPIS TABEL DO PLIKÓW
    basic_stats.to_csv("table_basic_stats.csv", sep=";")
    win_table.to_csv("table_win_rate.csv", sep=";")
    rank_table.to_csv("table_ranks.csv", sep=";")
    dom_counts.to_csv("table_dominance_counts.csv", sep=";")
    dom_percent.to_csv("table_dominance_percent.csv", sep=";")

    # 8) WERSJE LaTeX
    basic_stats.to_latex(
        "table_basic_stats.tex",
        caption="Statystyki opisowe długości ścieżki dla poszczególnych algorytmów.",
        label="tab:basic_stats",
        float_format="%.3f",
    )
    win_table.to_latex(
        "table_win_rate.tex",
        caption="Odsetek instancji, w których algorytm był najlepszy (win rate).",
        label="tab:win_rate",
        float_format="%.3f",
    )
    rank_table.to_latex(
        "table_ranks.tex",
        caption="Średni ranking algorytmów na instancjach (1 = najlepszy).",
        label="tab:ranks",
        float_format="%.3f",
    )
    dom_counts.to_latex(
        "table_dominance_counts.tex",
        caption="Macierz dominacji: liczba instancji, w których algorytm w wierszu był lepszy niż algorytm w kolumnie.",
        label="tab:dominance_counts",
        float_format="%.0f",
    )
    dom_percent.to_latex(
        "table_dominance_percent.tex",
        caption="Macierz dominacji: odsetek instancji, w których algorytm w wierszu był lepszy niż algorytm w kolumnie.",
        label="tab:dominance_percent",
        float_format="%.3f",
    )

    print("\n[INFO] Zapisano:")
    print("  - table_basic_stats.(csv/tex)")
    print("  - table_win_rate.(csv/tex)")
    print("  - table_ranks.(csv/tex)")
    print("  - table_dominance_counts.(csv/tex)")
    print("  - table_dominance_percent.(csv/tex)")


if __name__ == "__main__":
    main()
