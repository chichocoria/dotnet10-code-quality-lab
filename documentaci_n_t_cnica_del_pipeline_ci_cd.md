# Documentación Técnica: Pipeline CI/CD Unificado de Calidad y Seguridad (.NET 10)

Este documento detalla la arquitectura, configuración, flujo de ejecución y administración del pipeline implementado en `.github/workflows/code-quality.yml`.

---

## 1. Arquitectura General del Pipeline

El pipeline integra tres capas de inspección estática que se ejecutan de manera concurrente en GitHub Actions y consolidan todos sus resultados mediante el estándar **SARIF v2.1.0** en el panel de **GitHub Code Scanning**:

```
                       +---------------------------------------+
                       | Disparadores: Push / PR / Dispatch    |
                       +-------------------+-------------------+
                                           |
                   +-----------------------+-----------------------+
                   |                                               |
                   v                                               v
       +-----------------------+                       +-----------------------+
       |   JOB 1: scan-secrets |                       | JOB 2: analyze-quality|
       +-----------+-----------+                       +-----------+-----------+
                   |                                               |
         [ Checkout fetch:0 ]                            [ Setup .NET 10 SDK ]
                   |                                               |
         [ Gitleaks Container]                           [ dotnet format check ]
        (Entropía + Regex Rules)                                   |
                   |                                     [ CodeQL Init (C#) ]
                   |                                    (security-and-quality)
                   |                                               |
                   |                                     [ dotnet build Release]
                   |                                     (Roslyn SARIF export)
                   |                                               |
                   |                                     [ CodeQL Analyze AST]
                   |                                               |
                   v                                               v
        { gitleaks.sarif }                              { roslyn.sarif & codeql }
                   |                                               |
                   +-----------------------+-----------------------+
                                           |
                                           v
                       +---------------------------------------+
                       | GitHub Security > Code Scanning Panel |
                       | - /language:secrets-gitleaks          |
                       | - /language:csharp-roslyn             |
                       | - /language:csharp-codeql             |
                       +---------------------------------------+
```

---

## 2. Eventos Disparadores (`on`) y Permisos (`permissions`)

### Triggers configurados
```yaml
on:
  push:
    branches: [ "main" ]
  pull_request:
    branches: [ "main" ]
  workflow_dispatch:
```
* **`push` a `main`:** Asegura que la rama productiva mantenga un estado limpio y actualizado en el panel general de seguridad.
* **`pull_request` a `main`:** Activa la inspección durante la revisión de código. Permite que GitHub comente las advertencias y vulnerabilidades directamente sobre los cambios propuestos antes de autorizar el merge.
* **`workflow_dispatch`:** Permite forzar la ejecución manual desde la interfaz web de GitHub Actions sin necesidad de enviar commits.

### Configuración de Permisos
```yaml
permissions:
  contents: read
  security-events: write
  actions: read
```
* **`contents: read`:** Permite clonar el código fuente del repositorio.
* **`security-events: write`:** Permite al token efímero `GITHUB_TOKEN` interactuar con la API de GitHub Advanced Security para cargar archivos `.sarif` y actualizar el estado de las alertas en la pestaña **Security**.
* **`actions: read`:** Permite consultar el estado de jobs y workflows asociados.

---

## 3. Desglose del Job 1: `scan-secrets` (Detección de Credenciales)

* **Objetivo:** Detectar claves privadas, tokens de nube, cadenas de conexión y contraseñas expuestas en archivos o en el historial de Git.
* **Runner:** `ubuntu-latest`.
* **Tiempo promedio de ejecución:** $\approx 15 - 30$ segundos.

### Paso a paso

#### Paso 1.1: `Checkout repository`
```yaml
- name: Checkout repository
  uses: actions/checkout@v4
  with:
    fetch-depth: 0
```
* **Aspecto crítico:** El parámetro `fetch-depth: 0` desactiva la clonación superficial (*shallow clone*) y descarga el historial completo de commits, tags y ramas. Gitleaks requiere este árbol completo para auditar si un secreto fue introducido en un commit antiguo aunque se haya borrado después.

#### Paso 1.2: `Run Gitleaks and export SARIF`
```yaml
- name: Run Gitleaks and export SARIF
  run: |
    docker run --rm \
      -v "${{ github.workspace }}:/path" \
      zricethezav/gitleaks:latest \
      detect \
      --source="/path" \
      --report-format=sarif \
      --report-path="/path/gitleaks.sarif" \
      --redact \
      --exit-code=0
```
* **`docker run --rm -v ...`:** Ejecuta la imagen oficial de Gitleaks en un contenedor efímero mapeando la raíz del workspace.
* **`detect --source="/path"`:** Ejecuta el modo de detección en todos los archivos e historial rastreado.
* **`--report-format=sarif --report-path=...`:** Convierte las detecciones al estándar SARIF v2.1.0 para que GitHub pueda interpretarlo nativamente.
* **`--redact`:** Enmascara el valor del secreto encontrado en el reporte (ej. reemplazando caracteres por `***`) para que las credenciales no queden legibles en los artefactos del pipeline.
* **`--exit-code=0`:** Evita que el contenedor falle con error ante un hallazgo. Esto garantiza que el paso siguiente siempre pueda subir el archivo SARIF a GitHub para su visualización interactiva.

#### Paso 1.3: `Upload Gitleaks SARIF`
```yaml
- name: Upload Gitleaks SARIF to GitHub Code Scanning
  uses: github/codeql-action/upload-sarif@v3
  if: always()
  with:
    sarif_file: "${{ github.workspace }}/gitleaks.sarif"
    category: "/language:secrets-gitleaks"
```
* **`if: always()`:** Asegura que el reporte se procese incluso si pasos previos reportaron advertencias.
* **`category: "/language:secrets-gitleaks"`:** Etiqueta los hallazgos en la base de datos de GitHub bajo su propia categoría para diferenciarlos de los problemas de código de Roslyn o CodeQL.

---

## 4. Desglose del Job 2: `analyze-quality` (Roslyn + CodeQL)

* **Objetivo:** Auditar calidad de código, formato, buenas prácticas de .NET 10 y vulnerabilidades complejas de inyección/taint analysis.
* **Runner:** `ubuntu-latest`.

### Paso a paso

#### Paso 2.1: `Setup .NET 10 SDK`
```yaml
- name: Setup .NET 10 SDK
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '10.0.x'
```
* Instala las herramientas de compilación, el runtime y los analizadores de C# correspondientes a .NET 10.

#### Paso 2.2: `Verify Format`
```yaml
- name: Verify Format (Estilo de código)
  run: dotnet format src/QualityLab.Api/QualityLab.Api.csproj --verify-no-changes --no-restore
  continue-on-error: true
```
* Comprueba si el código cumple con las reglas de estilo de `.editorconfig` o los estándares por defecto de .NET (espaciado, directivas `using`, indentación).
* `--verify-no-changes`: Si detecta archivos mal formateados, devuelve un código de error en lugar de formatearlos en disco.

#### Paso 2.3: `Initialize CodeQL`
```yaml
- name: Initialize CodeQL (Suite Security & Quality)
  uses: github/codeql-action/init@v3
  with:
    languages: csharp
    queries: security-and-quality
```
* Prepara el motor CodeQL para monitorear el compilador C#.
* **`queries: security-and-quality`:** A diferencia del análisis básico, activa reglas de mantenibilidad, código muerto (*dead code*), variables sin uso y patrones de rendimiento ineficientes.

#### Paso 2.4: `Build & Generate Roslyn SARIF`
```yaml
- name: Build & Generate Roslyn SARIF
  run: |
    dotnet restore src/QualityLab.Api/QualityLab.Api.csproj
    dotnet build src/QualityLab.Api/QualityLab.Api.csproj --configuration Release --no-restore
```
* Restaura dependencias y compila la Minimal API.
* Al tener configurada la propiedad `<ErrorLog>` en el archivo `.csproj`, el compilador Roslyn genera automáticamente el archivo `roslyn-analysis.sarif` en formato v2.1.0 durante la compilación.

#### Paso 2.5: `Perform CodeQL Analysis`
```yaml
- name: Perform CodeQL Analysis
  uses: github/codeql-action/analyze@v3
  with:
    category: "/language:csharp-codeql"
```
* Realiza el análisis semántico y de flujo de datos (*Taint Tracking*) sobre la base de datos construida durante el build. Identifica vulnerabilidades como inyecciones de comandos en llamadas al sistema operativo.

#### Paso 2.6: `Upload Roslyn SARIF`
```yaml
- name: Upload Roslyn SARIF to GitHub Code Scanning
  uses: github/codeql-action/upload-sarif@v3
  if: always()
  with:
    sarif_file: "${{ github.workspace }}/roslyn-analysis.sarif"
    category: "/language:csharp-roslyn"
```
* Publica las advertencias del compilador Roslyn (ej. `CA1826`, `CS0219`, `CS1998`) en el mismo panel de Code Scanning bajo la categoría `/language:csharp-roslyn`.

---

## 5. Resumen de Herramientas y Responsabilidades

| Herramienta | Nivel de Análisis | Tipos de Problemas Detectados | Categoría en GitHub |
| :--- | :--- | :--- | :--- |
| **Gitleaks** | Historial Git y Archivos planos | API Keys, credenciales AWS/Azure, cadenas de conexión, passwords. | `/language:secrets-gitleaks` |
| **Roslyn Compiler** | Sintáctico y Estructural (.NET 10) | Rendimiento (LINQ vs Property), código sin usar, async sin await, estilo. | `/language:csharp-roslyn` |
| **CodeQL** | Semántico y Flujo de Datos (AST) | Inyección de comandos/SQL, control de flujo inseguro, excepciones vacías. | `/language:csharp-codeql` |

---

## 6. Mantenimiento y Buenas Prácticas

1. **Paralelismo:** Los jobs `scan-secrets` y `analyze-quality` no tienen dependencia entre sí (`needs:`), por lo que GitHub Actions los ejecuta en máquinas virtuales paralelas, reduciendo el tiempo total de ejecución.
2. **Uso de Categorías SARIF:** El uso de `category:` en cada llamada a `upload-sarif` es fundamental para evitar que una herramienta sobrescriba los resultados de la otra en la interfaz de GitHub.
3. **Calibración de Alertas:** Cuando un hallazgo no aplique (ejemplo: un token falso en un test unitario), puede descartarse directamente desde la interfaz de GitHub marcándolo como *False Positive* o *Used in Tests*, conservando la trazabilidad de la auditoría.