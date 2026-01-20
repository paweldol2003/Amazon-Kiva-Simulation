import pandas as pd
import matplotlib.pyplot as plt


# ===============================
# 1. Wczytanie danych i przygotowanie pól
# ===============================

def load_and_prepare(path: str) -> pd.DataFrame:
    """Wczytuje CSV, konwertuje Success na bool, dodaje ID trajektorii."""
    df = pd.read_csv(path, sep=';')

    # Konwersja kolumny Success na bool
    if df['Success'].dtype == 'object':
        df['Success'] = df['Success'].map({
            'True': True, 'False': False,
            'true': True, 'false': False,
            '1': True, '0': False
        })
    elif pd.api.types.is_numeric_dtype(df['Success']):
        df['Success'] = df['Success'].astype(int).astype(bool)

    # Każde 3 wiersze = jedna trajektoria (ta sama trasa, różne algorytmy)
    df['Trajektoria'] = df.index // 3

    return df


# ===============================
# 2. Metryki: success, nadwyżki, optymalność, SoC, % wygranych
# ===============================

def compute_metrics(df: pd.DataFrame) -> tuple[pd.DataFrame, pd.DataFrame]:
    """
    Zwraca:
    - df_ok: tylko udane ścieżki z dodatnią długością
    - tabela: tabela zbiorcza z metrykami
    """

    # --- PULA A: skuteczność (wszystkie próby, bez względu na długość) ---
    success_rate = df.groupby('Algorithm')['Success'].mean() * 100  # w %

    # --- PULA B: tylko udane ścieżki z niezerową długością ---
    df_ok = df[(df['Success'] == True) & (df['PathLength'] > 0)].copy()
    df_ok['Nadwyżka'] = df_ok['PathLength'] - df_ok['Manhattan']
    df_ok['Nadwyżka%'] = df_ok['Nadwyżka'] / df_ok['Manhattan'] * 100
    df_ok['Optymalność'] = df_ok['PathLength'] / (df_ok['Manhattan'])

    # --- PULA C: trajektorie ukończone przez wszystkie algorytmy (do SoC) ---
    traj_ok_all = []
    for traj_id, grupa in df.groupby('Trajektoria'):
        # trajektoria w SoC tylko, jeśli wszystkie algorytmy:
        # - mają Success = True
        # - mają PathLength > 0
        if ((grupa['Success'] == True) & (grupa['PathLength'] > 0)).all():
            traj_ok_all.append(traj_id)

    df_soc = df[(df['Trajektoria'].isin(traj_ok_all)) &
                (df['Success'] == True) &
                (df['PathLength'] > 0)].copy()

    soc = df_soc.groupby('Algorithm')['PathLength'].sum()

    # --- % WYGRANYCH: PULA A/B (dla wszystkich udanych trajektorii) ---
    algs = df_ok['Algorithm'].unique()
    wins = {alg: 0 for alg in algs}
    counts_active = {alg: 0 for alg in algs}

    for traj_id, grupa in df_ok.groupby('Trajektoria'):
        active = grupa  # df_ok już ma tylko udane i PathLength > 0
        if len(active) == 0:
            continue

        min_len = active['PathLength'].min()
        winners = active[active['PathLength'] == min_len]['Algorithm']

        # każdy biorący udział w tej trajektorii
        for alg in active['Algorithm']:
            counts_active[alg] += 1

        # każdy zwycięzca (ex aequo możliwe)
        for w in winners:
            wins[w] += 1

    percent_wins = {}
    for alg in algs:
        if counts_active[alg] > 0:
            percent_wins[alg] = wins[alg] / counts_active[alg] * 100
        else:
            percent_wins[alg] = 0.0

    # --- TABELA KOŃCOWA METRYK ---
    tabela = df_ok.groupby('Algorithm').agg(
        srednia_dl=('PathLength', 'mean'),
        srednia_nadwyzka=('Nadwyżka', 'mean'),
        srednia_nadwyzka_proc=('Nadwyżka%', 'mean'),
        srednia_opt=('Optymalność', 'mean')
    )

    tabela['SoC [kroki]'] = soc
    tabela['Wygrane [%]'] = pd.Series(percent_wins)
    tabela['Skuteczność [%]'] = success_rate

    tabela = tabela.round({
        'srednia_dl': 2,
        'srednia_nadwyzka': 2,
        'srednia_nadwyzka_proc': 2,
        'srednia_opt': 3,
        'SoC [kroki]': 0,
        'Wygrane [%]': 2,
        'Skuteczność [%]': 2
    })

    tabela = tabela.rename(columns={
        'srednia_dl': 'Średnia długość ścieżki [kroki]',
        'srednia_nadwyzka': 'Średnia nadwyżka [kroki]',
        'srednia_nadwyzka_proc': 'Średnia nadwyżka [%]',
        'srednia_opt': 'Średni współczynnik optymalności'
    })

    tabela = tabela[
        [
            'Średnia długość ścieżki [kroki]',
            'SoC [kroki]',
            'Średnia nadwyżka [kroki]',
            'Średnia nadwyżka [%]',
            'Średni współczynnik optymalności',
            'Wygrane [%]',
            'Skuteczność [%]'
        ]
    ]

    return df_ok, tabela


# ===============================
# 3. WYKRESY – osobne funkcje
# ===============================

def plot_mean_path_vs_manhattan_all(df_ok: pd.DataFrame, bins: int = 15):
    """
    Średnia długość ścieżki względem Manhattan (bez scattera),
    wszystkie algorytmy na jednym wykresie.
    """
    df_tmp = df_ok.copy()

    # zabezpieczenie: rzutujemy Manhattan na liczbę (gdyby coś jednak było stringiem)
    df_tmp['Manhattan'] = pd.to_numeric(df_tmp['Manhattan'], errors='coerce')
    df_tmp = df_tmp.dropna(subset=['Manhattan', 'PathLength'])

    # ile naprawdę mamy unikalnych wartości Manhattan
    uniq = df_tmp['Manhattan'].nunique()
    if uniq < 2:
        print("Za mało różnych wartości Manhattan, żeby zrobić wykres.")
        return

    # liczba binów nie większa niż uniq-1
    local_bins = min(bins, uniq - 1)

    # JEDEN pd.cut na całym df_ok
    df_tmp['Manhattan_bin'] = pd.cut(df_tmp['Manhattan'], bins=local_bins)

    # średnie w binach osobno dla każdego algorytmu
    mean_by_bin = df_tmp.groupby(['Algorithm', 'Manhattan_bin']).agg(
        manh_mean=('Manhattan', 'mean'),
        path_mean=('PathLength', 'mean')
    ).reset_index().dropna()

    algs = sorted(df_tmp['Algorithm'].unique())
    max_manh = df_tmp['Manhattan'].max()

    plt.figure(figsize=(7, 5))
    plt.plot([0, max_manh], [0, max_manh], '--', color='gray', linewidth=1,
             label='Długość Manhattan (idealnie)')

    for alg in algs:
        sub = mean_by_bin[mean_by_bin['Algorithm'] == alg].sort_values('manh_mean')
        if sub.empty:
            continue
        plt.plot(sub['manh_mean'], sub['path_mean'],
                 marker='o', linewidth=2, label=alg)

    plt.xlabel('Długość Manhattan [kroki]')
    plt.ylabel('Średnia długość ścieżki [kroki]')
    plt.title('Średnia długość ścieżki względem Manhattan')
    plt.legend()
    plt.grid(alpha=0.3)
    plt.tight_layout()
    plt.show()



def plot_mean_path_vs_manhattan_separate(df_ok: pd.DataFrame, bins: int = 15):
    """
    Średnia długość ścieżki względem Manhattan dla każdego algorytmu osobno,
    wykresy ułożone jeden pod drugim.
    """
    algs = sorted(df_ok['Algorithm'].unique())
    n = len(algs)

    # n wierszy, 1 kolumna -> wykresy pod sobą
    fig, axes = plt.subplots(n, 1, figsize=(7, 3.5 * n), sharex=True, sharey=True)

    if n == 1:
        axes = [axes]

    max_manh = df_ok['Manhattan'].max()

    for ax, alg in zip(axes, algs):
        sub = df_ok[df_ok['Algorithm'] == alg].sort_values('Manhattan')

        if sub.empty:
            continue

        # liczba unikalnych wartości Manhattan
        uniq = sub['Manhattan'].nunique()
        if uniq < 2:
            # tylko jeden Manhattan – rysujemy punkt
            manh_mean = sub['Manhattan'].mean()
            path_mean = sub['PathLength'].mean()
            ax.plot([0, max_manh], [0, max_manh], '--', color='gray', linewidth=1)
            ax.plot(manh_mean, path_mean, marker='o')
        else:
            local_bins = min(bins, uniq - 1)
            sub['Manhattan_bin'] = pd.cut(sub['Manhattan'], bins=local_bins)

            mean_by_bin = sub.groupby('Manhattan_bin').agg(
                manh_mean=('Manhattan', 'mean'),
                path_mean=('PathLength', 'mean')
            ).dropna()

            ax.plot([0, max_manh], [0, max_manh], '--', color='gray', linewidth=1)
            ax.plot(mean_by_bin['manh_mean'], mean_by_bin['path_mean'],
                    marker='o', linewidth=2)

        ax.set_title(f'Algorytm: {alg}')
        ax.set_ylabel('Średnia długość ścieżki [kroki]')
        ax.grid(alpha=0.3)

    axes[-1].set_xlabel('Odległość Manhattan')

    plt.suptitle('Średnia długość ścieżki względem Manhattan – podział na algorytmy')
    plt.tight_layout(rect=[0, 0.03, 1, 0.95])
    plt.show()




def plot_boxplots_nadwyzka(df_ok: pd.DataFrame):
    """Boxplot nadwyżki absolutnej i procentowej - jeden pod drugim."""
    fig, axes = plt.subplots(2, 1, figsize=(7, 8), sharex=True)

    # --- Nadwyżka [kroki] ---
    df_ok.boxplot(column='Nadwyżka', by='Algorithm', ax=axes[0])
    axes[0].set_title('Nadwyżka długości ścieżki względem Manhattan [kroki]')
    axes[0].set_xlabel('')
    axes[0].set_ylabel('Nadwyżka [kroki]')
    
    # --- Nadwyżka [%] ---
    df_ok.boxplot(column='Nadwyżka%', by='Algorithm', ax=axes[1])
    axes[1].set_title('Nadwyżka długości ścieżki względem Manhattan [%]')
    axes[1].set_xlabel('Algorytm')
    axes[1].set_ylabel('Nadwyżka [%]')

    # sprzątanie Pandasa
    plt.suptitle('')
    plt.tight_layout()
    plt.show()


def plot_aco_failures_two_plots(df: pd.DataFrame, bins: int = 12):
    """
    Dwa wykresy dla ACO pod sobą:
    1. Liczba nieudanych ścieżek
    2. Procent nieudanych ścieżek
    względem Manhattan (z binami)
    """

    aco = df[df['Algorithm'] == 'ACO'].copy()
    aco['Manhattan'] = pd.to_numeric(aco['Manhattan'], errors='coerce')
    aco = aco.dropna(subset=['Manhattan'])

    uniq = aco['Manhattan'].nunique()
    if uniq < 2:
        print("Za mało różnych wartości Manhattan, aby zbudować wykres.")
        return

    bins = min(bins, uniq - 1)
    aco['Manhattan_bin'] = pd.cut(aco['Manhattan'], bins=bins)

    total = aco.groupby('Manhattan_bin').size()
    fail = aco[(aco['Success'] == False) | (aco['PathLength'] <= 0)].groupby('Manhattan_bin').size()
    fail = fail.reindex(total.index, fill_value=0)

    rate = (fail / total * 100).fillna(0)

    labels = [f"{int(interval.left)}–{int(interval.right)}" for interval in total.index]

    fig, axes = plt.subplots(2, 1, figsize=(8, 8), sharex=True)

    # --- wykres 1: liczba failów ---
    axes[0].bar(labels, fail, color='tab:red', alpha=0.7)
    axes[0].set_ylabel('Liczba nieudanych')
    axes[0].set_title('Nieudane trasy ACO względem Manhattan')
    axes[0].grid(axis='y', alpha=0.3)

    # --- wykres 2: procent failów ---
    axes[1].plot(labels, rate, marker='o', linewidth=2, color='tab:blue')
    axes[1].set_ylabel('Procent nieudanych [%]')
    axes[1].set_xlabel('Zakres odległości Manhattan')
    axes[1].grid(alpha=0.3)

    # czytelniejsze x
    plt.setp(axes[1].get_xticklabels(), rotation=45, ha='right')

    plt.tight_layout()
    plt.show()



def plot_opt_vs_path_common(df_ok: pd.DataFrame, bins: int = 15):
    """
    Średni współczynnik optymalności względem odległości Manhattan – wszystkie algorytmy na jednym wykresie.
    """
    df_tmp = df_ok.copy()
    df_tmp['Manhattan'] = pd.to_numeric(df_tmp['Manhattan'], errors='coerce')
    df_tmp = df_tmp.dropna(subset=['Manhattan', 'Optymalność'])

    algs = sorted(df_tmp['Algorithm'].unique())
    uniq = df_tmp['Manhattan'].nunique()

    if uniq < 2:
        print("Za mało różnych wartości Manhattan.")
        return

    bins = min(bins, uniq - 1)
    df_tmp['Manhattan_bin'] = pd.cut(df_tmp['Manhattan'], bins=bins)

    mean_all = (
        df_tmp.groupby(['Algorithm', 'Manhattan_bin'])
        .agg(
            manh_mean=('Manhattan', 'mean'),
            opt_mean=('Optymalność', 'mean')
        )
        .reset_index()
        .dropna()
    )

    plt.figure(figsize=(7,5))
    for alg in algs:
        sub = mean_all[mean_all['Algorithm'] == alg].sort_values('manh_mean')
        plt.plot(sub['manh_mean'], sub['opt_mean'], marker='o', linewidth=2, label=alg)

    plt.xlabel('Odległość Manhattan')
    plt.ylabel('Średni współczynnik optymalności')
    plt.title('Współczynnik optymalności względem odległości Manhattan')
    plt.grid(alpha=0.3)
    plt.legend()
    plt.tight_layout()
    plt.show()


def plot_opt_vs_path_separate(df_ok: pd.DataFrame, bins: int = 15):
    """
    Średni współczynnik optymalności względem odległości Manhattan – osobno dla każdego algorytmu.
    Wykresy jeden pod drugim.
    """
    df_tmp = df_ok.copy()
    df_tmp['Manhattan'] = pd.to_numeric(df_tmp['Manhattan'], errors='coerce')
    df_tmp = df_tmp.dropna(subset=['Manhattan', 'Optymalność'])

    algs = sorted(df_tmp['Algorithm'].unique())
    n = len(algs)

    if n == 0:
        print("Brak danych.")
        return

    fig, axes = plt.subplots(n, 1, figsize=(7, 3.2*n), sharex=True)

    if n == 1:
        axes = [axes]

    for ax, alg in zip(axes, algs):
        sub = df_tmp[df_tmp['Algorithm'] == alg].sort_values('Manhattan')

        uniq = sub['Manhattan'].nunique()
        if uniq < 2:
            manh_mean = sub['Manhattan'].mean()
            opt_mean = sub['Optymalność'].mean()
            ax.plot(manh_mean, opt_mean, marker='o')
        else:
            local_bins = min(bins, uniq - 1)
            sub['Manhattan_bin'] = pd.cut(sub['Manhattan'], bins=local_bins)

            mean_sub = (
                sub.groupby('Manhattan_bin')
                .agg(
                    manh_mean=('Manhattan', 'mean'),
                    opt_mean=('Optymalność', 'mean')
                )
                .dropna()
            )

            ax.plot(mean_sub['manh_mean'], mean_sub['opt_mean'],
                    marker='o', linewidth=2)

        ax.set_title(f'Algorytm: {alg}')
        ax.set_ylabel('Średni współczynnik optymalności')
        ax.grid(alpha=0.3)

    axes[-1].set_xlabel('Odległość Manhattan')
    plt.tight_layout()
    plt.show()



def plot_rank_distributions(df_ok: pd.DataFrame):
    """Rozkład miejsc 1/2/3 dla algorytmów (stacked bar + bump chart)."""
    # sortujemy po trajektorii i długości ścieżki
    df_ok_sorted = df_ok.sort_values(['Trajektoria', 'PathLength'])
    df_ok_sorted['Miejsce'] = df_ok_sorted.groupby('Trajektoria').cumcount() + 1  # 1 = najkrótsza

    # ile razy algorytm był na miejscu 1/2/3
    rank_counts = df_ok_sorted.groupby(['Algorithm', 'Miejsce']).size().unstack(fill_value=0)

    # Wersja procentowa (udział procentowy miejsc 1/2/3 dla każdego algorytmu)
    rank_pct = rank_counts.div(rank_counts.sum(axis=1), axis=0) * 100

    # Wykres słupkowy skumulowany – procentowy
    ax = rank_pct.plot(kind='bar', stacked=True, figsize=(7, 5))
    plt.xlabel('Algorytm')
    plt.ylabel('Udział [%]')
    plt.title('Procentowy rozkład zajmowanych miejsc (1/2/3)')
    plt.legend(title='Miejsce', loc='upper right')
    plt.tight_layout()
    plt.show()

    # bump chart: średnia pozycja w koszykach Manhattan
    df_ok_sorted['Manhattan_bin'] = pd.cut(df_ok_sorted['Manhattan'], bins=10)

    rank_plot = df_ok_sorted.groupby(['Manhattan_bin', 'Algorithm'])['Miejsce'].mean().unstack()

    # zamiana etykiet: środek przedziału
    rank_plot.index = rank_plot.index.map(lambda x: round((x.left + x.right)/2, 1))

    plt.figure(figsize=(8,5))
    for alg in rank_plot.columns:
        plt.plot(rank_plot.index, rank_plot[alg], marker='o', label=alg)

    plt.gca().invert_yaxis()
    plt.xlabel('Długość Manhattan [kroki]')
    plt.ylabel('Średnia pozycja')
    plt.title('Ranking pozycji algorytmów względem długości Manhattan')
    plt.legend(title='Algorytm')
    plt.grid(alpha=0.3)
    plt.tight_layout()
    plt.show()

def plot_path_distribution_vs_manhattan(df_ok: pd.DataFrame, bins: int = 20):
    """
    Rozkład ścieżek: liczba unikalnych tras (trajektorii) w zależności od
    odległości Manhattan.
    """

    # schodzimy do poziomu trajektorii – jedna wartość Manhattan na trasę
    traj = (
        df_ok
        .sort_values('Algorithm')  # cokolwiek, chodzi o to, żeby mieć deterministycznie "pierwszy" wiersz
        .groupby('Trajektoria')
        .first()
        .reset_index()
    )

    traj['Manhattan'] = pd.to_numeric(traj['Manhattan'], errors='coerce')
    traj = traj.dropna(subset=['Manhattan'])

    # histogram / słupki
    plt.figure(figsize=(7, 5))
    plt.hist(traj['Manhattan'], bins=bins, edgecolor='black', alpha=0.7)
    plt.xlabel('Odległość Manhattan')
    plt.ylabel('Liczba ścieżek')
    plt.title('Rozkład długości ścieżek (Manhattan)')
    plt.grid(axis='y', alpha=0.3)
    plt.tight_layout()
    plt.show()


# ===============================
# 4. GŁÓWNY BLOK – wybierasz co chcesz odpalić
# ===============================

if __name__ == "__main__":
    plik = "dane.csv"

    df = load_and_prepare(plik)
    df_ok, tabela = compute_metrics(df)

    print("TABELA ZBIORCZA:\n", tabela, "\n")

    # wykresy – odkomentuj to, czego potrzebujesz
    # plot_mean_path_vs_manhattan_all(df_ok)
    # plot_mean_path_vs_manhattan_separate(df_ok)
    # plot_boxplots_nadwyzka(df_ok)
    # plot_opt_vs_path_common(df_ok, bins=15)
    # plot_opt_vs_path_separate(df_ok, bins=15)
    #plot_rank_distributions(df_ok)
    # plot_aco_failures_two_plots(df, bins=12)

    plot_path_distribution_vs_manhattan(df_ok, bins=20)
