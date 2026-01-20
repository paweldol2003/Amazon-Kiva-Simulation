import pandas as pd
import numpy as np

# Ścieżki do plików (dostosuj nazwy jeśli inne)
paths = {
    'ACO': 'ACOConvergenceLog.csv',
    'Firefly': 'FAConvergenceLog.csv',
    'Camel': 'CHAConvergenceLog.csv'
}

def classify_range(manh: float) -> str:
    if manh <= 40:
        return 'krótkie (M ≤ 40)'
    elif manh <= 80:
        return 'średnie (41 ≤ M ≤ 80)'
    else:
        return 'długie (M > 80)'

all_counts = {}

for alg_name, path in paths.items():
    print(f"\nŁadowanie: {alg_name} ({path})")

    df = pd.read_csv(path, sep=';', decimal=',')

    # upewniamy się że wartości numeryczne są numeryczne
    df['TimeMs'] = pd.to_numeric(df['TimeMs'], errors='coerce')
    df['Manhattan'] = pd.to_numeric(df['Manhattan'], errors='coerce')

    # ===========================
    # AUTOMATYCZNE WYKRYCIE RunId
    # kiedy TimeMs się cofa => nowy run
    # ===========================
    df = df.sort_index()  # zachowuje kolejność oryginalną
    df['RunId'] = (df['TimeMs'].diff() < 0).cumsum()

    # teraz mamy jeden Manhattan na Run:
    manh_per_run = df.groupby('RunId')['Manhattan'].first()

    # klasyfikacja
    ranges = manh_per_run.apply(classify_range)
    counts = ranges.value_counts()

    all_counts[alg_name] = counts

    print(counts)

# ===========================
# SUMA GLOBALNA (opcjonalnie)
# ===========================
print("\n=== SUMA GLOBALNA ===")
all_sum = sum(all_counts.values())
print(all_sum)
