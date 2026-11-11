# .NET 10 GitHub Code Quality Lab

Este laboratorio demuestra la integración nativa de **GitHub Code Scanning (CodeQL)** y los analizadores de **Roslyn (.NET 10)** con exportación a formato **SARIF**.

## Estructura
```text
dotnet10-code-quality-lab/
├── .github/
│   └── workflows/
│       └── code-quality.yml
├── src/
│   └── QualityLab.Api/
│       ├── QualityLab.Api.csproj
│       └── Program.cs
├── .gitignore
└── README.md
```

## Pasos para usarlo
1. Crea un repositorio público en GitHub.
2. Descomprime y haz push de estos archivos al repo:
   ```bash
   git init
   git add .
   git commit -m "feat: initial dotnet 10 code quality lab"
   git branch -M main
   git remote add origin <URL_DE_TU_REPO>
   git push -u origin main
   ```
3. GitHub Actions ejecutará automáticamente el workflow.
4. Revisa los resultados en la pestaña **Security > Code scanning**.
