import pandas as pd

df = pd.read_csv("dane.csv", sep=';')

# konwersja kolumny success jeśli potrzeba
if df['Success'].dtype == 'object':
    df['Success'] = df['Success'].map({'True': True, 'False': False})

# skuteczność
skutecznosc = df.groupby('Algorithm')['Success'].mean() * 100

# filtrujemy tylko udane próby
df_ok = df[df['Success'] == True].copy()

# współczynnik optymalności
df_ok['Optymalność'] = df_ok['PathLength'] / df_ok['Manhattan']

# agregacja jakości
wyniki = df_ok.groupby('Algorithm').agg(
    srednia_dl=('PathLength', 'mean'),
    odchylenie=('PathLength', 'std'),
    minimum=('PathLength', 'min'),
    maksimum=('PathLength', 'max'),
    srednia_opt=('Optymalność', 'mean')
)

# dodajemy skuteczność
wyniki['skutecznosc [%]'] = skutecznosc

print(wyniki)


import matplotlib.pyplot as plt

df_ok.boxplot(column='PathLength', by='Algorithm')
plt.title('Zmienność długości ścieżek')
plt.suptitle('')
plt.ylabel('Długość ścieżki [kroki]')
plt.xlabel('Algorytm')
plt.show()


import seaborn as sns
import matplotlib.pyplot as plt

# PathLength
for alg in df_ok['Algorithm'].unique():
    sns.kdeplot(
        df_ok[df_ok['Algorithm'] == alg]['PathLength'],
        label=f'{alg} (Path)',
        linewidth=2
    )

# Manhattan
sns.kdeplot(
    df_ok['Manhattan'],
    label='Manhattan (idealnie)',
    linewidth=2,
    color='black',
    linestyle='--'
)

plt.xlabel('Długość ścieżki [kroki]')
plt.ylabel('Gęstość')
plt.title('Porównanie ścieżek algorytmicznych z Manhattan')
plt.legend()
plt.show()


import pandas as pd

df = pd.read_csv("dane.csv", sep=';')

# filtrujemy tylko udane wykonania
if df['Success'].dtype == 'object':
    df['Success'] = df['Success'].map({'True': True, 'False': False})

df_ok = df[df['Success'] == True].copy()

# wyliczenie nadwyżki (kroki + %)
df_ok['Nadwyzka'] = df_ok['PathLength'] - df_ok['Manhattan']
df_ok['NadwyzkaProc'] = df_ok['Nadwyzka'] / df_ok['Manhattan'] * 100

# agregacja po algorytmie
tabela_nadwyzki = df_ok.groupby('Algorithm').agg(
    srednia_nadwyzka=('Nadwyzka', 'mean'),
    std_nadwyzki=('Nadwyzka', 'std'),
    nadwyzka_min=('Nadwyzka', 'min'),
    nadwyzka_max=('Nadwyzka', 'max'),
    srednia_nadwyzka_proc=('NadwyzkaProc', 'mean')
).reset_index()

print(tabela_nadwyzki)
