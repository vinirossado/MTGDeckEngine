{{- define "mtg.fullname" -}}
{{- printf "%s" .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "mtg.labels" -}}
app.kubernetes.io/name: {{ include "mtg.fullname" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: mtg-deck-engine
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
{{- end -}}

{{- define "mtg.selectorLabels" -}}
app: {{ include "mtg.fullname" . }}
{{- end -}}
