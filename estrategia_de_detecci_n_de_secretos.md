# Estrategia de Detección de Secretos: GitHub Secret Scanning y Gitleaks

Este documento detalla el funcionamiento, arquitectura, implementación y complementariedad de **GitHub Secret Scanning** y **Gitleaks** para prevenir y remediar la exposición accidental de credenciales, tokens y variables sensibles en repositorios de código.

---

## 1. Visión General: El problema de las credenciales expuestas

La fuga involuntaria de secretos (API keys, certificados, tokens de acceso a la nube, contraseñas de bases de datos) en repositorios Git representa uno de los vectores de ataque más comunes y críticos:
- **Persistencia en el historial:** Un secreto commiteado permanece en los metadatos y objetos del árbol Git, aun si se elimina en un commit posterior.
- **Indexación automatizada:** Bots y crawlers externos escanean continuamente repositorios públicos en busca de tokens recién publicados.
- **Riesgo en repositorios privados:** Exponer credenciales internamente amplía indebidamente la superficie de acceso lateral (*privilege escalation* interno).

---

## 2. GitHub Secret Scanning (Nativo)

### ¿Qué es y cómo opera?
GitHub Secret Scanning es un servicio gestionado directamente por GitHub. Analiza de manera asíncrona cada operación de `push` contra un catálogo estandarizado de expresiones regulares provistas por más de 200 socios comerciales (AWS, Azure, Google Cloud, Stripe, OpenAI, Slack, GitHub, HashiCorp, etc.).

### Capacidades clave
1. **GitHub Partner Program (Revocación Automática):**
   - Cuando se detecta un token válido de un socio en un repositorio público, GitHub notifica de forma segura a dicho proveedor en tiempo casi real.
   - Proveedores como AWS o Azure revocan el token automáticamente o alertan al propietario antes de que pueda ser explotado.
2. **Push Protection (Bloqueo Preventivo):**
   - En lugar de limitarse a detectar el secreto una vez dentro del historial, intercepta la operación `git push` desde el cliente local.
   - Si detecta un patrón conocido, rechaza el push en consola con un mensaje de error y un enlace web para confirmar si se trata de un falso positivo o revocarlo de inmediato.
3. **Gestión sin configuración YAML:**
   - No requiere mantener workflows ni runners; se administra a nivel repositorio, organización o empresa.

### Limitaciones de GitHub Secret Scanning
- **Orientado a firmas estructuradas:** Requiere que el formato del secreto coincida con patrones bien documentados por los proveedores asociados.
- **Baja cobertura para secretos genéricos:** No alerta típicamente sobre contraseñas de bases de datos internas (`Server=...;Password=admin123;`), archivos de claves privadas personalizadas, o variables de entorno locales como `JWT_SECRET=miClave123`.

---

## 3. Gitleaks (Open Source)

### ¿Qué es y cómo opera?
Gitleaks es una herramienta *open source* (escrita en Go) diseñada para auditar repositorios Git, archivos locales y flujos de CI/CD. Utiliza una combinación de:
- **Expresiones regulares extensibles:** Más de 160 reglas predefinidas y capacidad de agregar reglas corporativas propias.
- **Análisis de entropía de Shannon:** Evalúa la aleatoriedad matemática de las cadenas de texto para detectar contraseñas generadas o tokens opacos que no responden a una firma conocida.

### Capacidades clave
1. **Auditoría profunda del historial (`fetch-depth: 0`):**
   - Examina ramas, tags, stashes y cada commit histórico desde la creación del repositorio.
2. **Salida nativa en estándar SARIF (v2.1.0):**
   - Permite canalizar sus hallazgos directamente hacia la pestaña **Security > Code scanning** de GitHub junto con CodeQL y Roslyn.
3. **Ofuscación (`--redact`):**
   - Enmascara los valores reales encontrados en los reportes de consola y archivos generados para evitar que el log del pipeline se convierta en una segunda fuente de filtración.
4. **Shift-Left (Pre-commit hooks):**
   - Puede ejecutarse localmente en la máquina del desarrollador antes de que el commit sea creado.

### Limitaciones de Gitleaks
- **Posibles falsos positivos:** El cálculo por entropía puede clasificar identificadores aleatorios (como UUIDs o hashes de compilación) como posibles contraseñas si no se configuran listas de exclusión (*allowlists*).
- **Consumo de recursos en repositorios masivos:** Analizar un historial Git de varios gigabytes con miles de commits requiere tiempo de cómputo en el runner.

---

## 4. Comparativa Técnica

| Característica | GitHub Secret Scanning | Gitleaks |
| :--- | :--- | :--- |
| **Licencia** | Propietario (Gratis en repos públicos; GHAS en privados) | Open Source (Licencia MIT) |
| **Punto de ejecución** | Servidores de GitHub (Push / Background) | Local (Pre-commit), Runner CI/CD, Contenedores |
| **Prevención activa** | Push Protection (bloquea el push en terminal) | CLI hook (bloquea el commit local o falla el PR) |
| **Revocación de tokens** | Sí (mediante el Partner Program con proveedores) | No (únicamente detecta y reporta) |
| **Secretos corporativos/custom** | Requiere GitHub Enterprise para definir patrones | Totalmente personalizable mediante archivo `.gitleaks.toml` |
| **Detección por Entropía** | Limitada | Sí (Shannon Entropy integrada) |
| **Integración con SARIF** | Panel propio de alertas en GitHub Security | Exporta a SARIF v2.1 para GitHub Code Scanning |

---

## 5. Implementación Paso a Paso

### A. Configurar GitHub Secret Scanning + Push Protection
1. En tu repositorio de GitHub, dirígete a **Settings**.
2. En la barra lateral izquierda, navega a **Code security and analysis**.
3. En la sección **Secret scanning**, verifica que esté en estado **Enabled**.
4. Marca la casilla **Push protection**. A partir de ese momento, cualquier comando `git push` con un token detectado será rechazado en la terminal.

### B. Implementar Gitleaks en GitHub Actions con salida SARIF

En `.github/workflows/code-quality.yml`, incluye el siguiente job:

```yaml
  scan-secrets:
    name: "Gitleaks Secret Scan"
    runs-on: ubuntu-latest
    permissions:
      contents: read
      security-events: write

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4
        with:
          fetch-depth: 0 # Descarga todo el historial de commits

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

      - name: Upload Gitleaks SARIF to GitHub Code Scanning
        uses: github/codeql-action/upload-sarif@v3
        if: always()
        with:
          sarif_file: "${{ github.workspace }}/gitleaks.sarif"
          category: "/language:secrets-gitleaks"
```

### C. Personalización con `.gitleaks.toml` (Opcional)

Si necesitas detectar secretos con patrones propios de tu organización o excluir archivos de prueba, añade un archivo `.gitleaks.toml` en la raíz del repositorio:

```toml
title = "Reglas de Secretos Personalizadas"

# Regla propia: Detectar cadenas de conexión internas
[[rules]]
id = "internal-db-connstring"
description = "Cadena de conexión SQL interna con contraseña hardcodeada"
regex = '''(?i)(Password|pwd)\s*=\s*['"][^'"]+['"]'''
entropy = 3.5
severity = "high"

# Exclusiones de carpetas o archivos de tests
[allowlist]
description = "Archivos de prueba ignorados"
paths = [
  '''tests/QualityLab.Tests/MockData/.*''',
  '''\.sarif$'''
]
```

---

## 6. Modelo de Complementariedad (Defense-in-Depth)

La mejor práctica en ingeniería de software consiste en combinar ambas herramientas en una arquitectura en capas:

```
[ Desarrollador ]
       │
       ├──> 1. Gitleaks local (Pre-commit hook)
       │    └─ Detecta secretos antes de commitear en la máquina local.
       │
       ├──> 2. GitHub Push Protection
       │    └─ Rechaza el push en la terminal si se intenta subir un token conocido.
       │
       ├──> 3. GitHub Actions CI (Gitleaks SARIF)
       │    └─ Audita PRs, evalúa entropía y genera alertas en Code Scanning.
       │
       └──> 4. GitHub Secret Scanning (Background Engine)
            └─ Notifica y revoca credenciales reales con los proveedores cloud.
```

### Protocolo de remediación si un secreto es publicado
1. **Revocar inmediatamente:** Considerar la credencial comprometida y rotarla en el proveedor correspondiente. Eliminar el commit **no** anula el riesgo de que ya haya sido indexada.
2. **Reescribir el historial (si aplica):** Utilizar herramientas como `git-filter-repo` o BFG Repo-Cleaner para purgar el secreto de todos los commits anteriores del árbol de Git.
3. **Resolver la alerta en GitHub:** Marcar la alerta en **Security > Secret scanning** o **Code scanning** con el estado correspondiente (*Revoked*, *False positive* o *Used in tests*).