import pandas as pd
import matplotlib.pyplot as plt
import seaborn as sns

# 1. Wczytanie danych
# Pamiętaj, żeby plik CSV był w tym samym folderze co skrypt
df = pd.read_csv('AlgorithmResults.csv', delimiter=';')

# 2. Czyszczenie danych
# Zamiana przecinków na kropki w kolumnie TimeMs (jeśli są stringami)
if df['TimeMs'].dtype == object:
    df['TimeMs'] = df['TimeMs'].astype(str).str.replace(',', '.').astype(float)

# Filtrowanie tylko udanych prób dla statystyk długości/efektywności
success_df = df[df['Success'] == True].copy()
success_df['Efficiency'] = success_df['PathLength'] / success_df['Manhattan']

# Ustawienia stylu (żeby wykresy były czytelne w pracy dyplomowej)
sns.set_style("whitegrid")
plt.rcParams.update({'font.size': 12, 'figure.dpi': 300})

# --- WYKRES 1: Czas Wykonania (Boxplot) ---
plt.figure(figsize=(8, 6))
sns.boxplot(x='Algorithm', y='TimeMs', data=df, palette='viridis')
plt.title('Rozkład czasu wykonywania algorytmów')
plt.ylabel('Czas [ms]')
plt.xlabel('Algorytm')
plt.tight_layout()
plt.savefig('czas_wykonania.png')
plt.close()

# --- WYKRES 2: Efektywność Ścieżki (Boxplot) ---
plt.figure(figsize=(8, 6))
sns.boxplot(x='Algorithm', y='Efficiency', data=success_df, palette='viridis')
plt.axhline(1.0, color='r', linestyle='--', label='Optimum (Manhattan)')
plt.title('Efektywność ścieżki (1.0 = idealna)')
plt.ylabel('Stosunek: Długość / Manhattan')
plt.xlabel('Algorytm')
plt.legend()
plt.tight_layout()
plt.savefig('efektywnosc.png')
plt.close()

# --- WYKRES 3: Wskaźnik Sukcesu (Barplot) ---
success_rate = df.groupby('Algorithm')['Success'].mean().reset_index()
success_rate['Success'] *= 100 # Zamiana na procenty

plt.figure(figsize=(8, 6))
ax = sns.barplot(x='Algorithm', y='Success', data=success_rate, palette='viridis')
plt.title('Wskaźnik sukcesu (Success Rate)')
plt.ylabel('Sukces [%]')
plt.ylim(0, 105)

# Dodanie etykiet z procentami nad słupkami
for i, v in enumerate(success_rate['Success']):
    ax.text(i, v + 1, f"{v:.1f}%", ha='center', fontweight='bold')

plt.tight_layout()
plt.savefig('sukces.png')
plt.close()

print("Wykresy zostały zapisane: czas_wykonania.png, efektywnosc.png, sukces.png")