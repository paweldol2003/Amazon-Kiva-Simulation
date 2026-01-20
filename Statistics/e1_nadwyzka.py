import pandas as pd

# === 1. Wczytanie danych ===

plik = "dane.csv"  # zmień na własną ścieżkę
df = pd.read_csv(plik, sep=';')

# Upewniamy się, że kolumna Success jest booleana
if df['Success'].dtype == 'object':
    df['Success'] = df['Success'].map({'True': True, 'False': False, 'true': True, 'false': False})
elif pd.api.types.is_numeric_dtype(df['Success']):
    df['Success'] = df['Success'].astype(int).astype(bool)

# Zakładamy, że co 3 wiersze to ta sama trajektoria
df['Trajektoria'] = df.index // 3

# === 2. Pula A – procent udanych ścieżek (Success%) ===

# Success liczymy po wszystkich próbach, niezależnie od długości
success_rate = df.groupby('Algorithm')['Success'].mean() * 100  # w %


# === 3. Pula B – tylko udane ścieżki z niezerową długością ===

df_ok = df[(df['Success'] == True) & (df['PathLength'] > 0)].copy()

# Nadwyżki względem Manhattan
df_ok['Nadwyżka'] = df_ok['PathLength'] - df_ok['Manhattan']
df_ok['Nadwyżka%'] = df_ok['Nadwyżka'] / df_ok['Manhattan'] * 100
df_ok['Optymalność'] = df_ok['PathLength'] / df_ok['Manhattan']


# === 4. Pula C – trajektorie ukończone przez wszystkie algorytmy (do SoC) ===

traj_ok_all = []
for traj_id, grupa in df.groupby('Trajektoria'):
    # trajektoria liczona do SoC tylko, jeśli wszystkie algorytmy:
    #   - mają Success = True
    #   - mają PathLength > 0
    if all((grupa['Success'] == True) & (grupa['PathLength'] > 0)):
        traj_ok_all.append(traj_id)

# wybieramy tylko wiersze z kompletnych trajektorii i udanych ścieżek
df_soc = df[(df['Trajektoria'].isin(traj_ok_all)) &
            (df['Success'] == True) &
            (df['PathLength'] > 0)].copy()

# SoC = suma długości ścieżek po algorytmach
soc = df_soc.groupby('Algorithm')['PathLength'].sum()


# === 5. % wygranych – PULA A (na wszystkich trajektoriach, gdzie algorytm miał poprawny wynik) ===

algs = df_ok['Algorithm'].unique()
wins = {alg: 0 for alg in algs}
counts_active = {alg: 0 for alg in algs}

for traj_id, grupa in df_ok.groupby('Trajektoria'):
    # aktywne algorytmy w tej trajektorii (wszystkie w df_ok są Success=True i PathLength>0)
    active = grupa

    if len(active) == 0:
        continue

    # minimalna długość ścieżki w tej trajektorii
    min_len = active['PathLength'].min()

    # algorytmy, które mają tę minimalną długość (ex aequo → wszyscy dostają "win")
    winners = active[active['PathLength'] == min_len]['Algorithm']

    # każdy algorytm, który brał udział w tej trajektorii, zwiększa licznik udziału
    for alg in active['Algorithm']:
        counts_active[alg] += 1

    # zwycięzcy dostają punkt
    for w in winners:
        wins[w] += 1

# procent wygranych = wins / liczba trajektorii, w których algorytm brał udział
percent_wins = {}
for alg in algs:
    if counts_active[alg] > 0:
        percent_wins[alg] = wins[alg] / counts_active[alg] * 100
    else:
        percent_wins[alg] = 0.0


# === 6. Agregacja metryk jakościowych (Pula B) ===

tabela = df_ok.groupby('Algorithm').agg(
    srednia_dl=('PathLength', 'mean'),
    srednia_nadwyzka=('Nadwyżka', 'mean'),
    srednia_nadwyzka_proc=('Nadwyżka%', 'mean'),
    srednia_opt=('Optymalność', 'mean')
)

# dodajemy SoC (tylko z trajektorii kompletnych)
tabela['SoC [kroki]'] = soc

# dodajemy % wygranych
tabela['Wygrane [%]'] = pd.Series(percent_wins)

# dodajemy % udanych ścieżek
tabela['Skuteczność [%]'] = success_rate

# zaokrąglenia
tabela = tabela.round({
    'srednia_dl': 2,
    'srednia_nadwyzka': 2,
    'srednia_nadwyzka_proc': 2,
    'srednia_opt': 3,
    'SoC [kroki]': 0,
    'Wygrane [%]': 2,
    'Skuteczność [%]': 2
})

# ładniejsze nazwy kolumn po polsku
tabela = tabela.rename(columns={
    'srednia_dl': 'Średnia długość ścieżki [kroki]',
    'srednia_nadwyzka': 'Średnia nadwyżka [kroki]',
    'srednia_nadwyzka_proc': 'Średnia nadwyżka [%]',
    'srednia_opt': 'Średni współczynnik optymalności'
})

# ewentualna kolejność kolumn
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

print(tabela)
