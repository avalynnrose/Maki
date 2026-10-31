import { Alert, Button, Group, Text } from '@mantine/core'
import { Trans, useLingui } from '@lingui/react/macro'
import { t as now } from '@lingui/core/macro'
import { notifications } from '@mantine/notifications'
import { IconLanguage } from '@tabler/icons-react'
import { useRevertLanguageFallback, useSeriesLanguageFallback } from '../api/hooks'
import { languageName } from '../api/titles'
import { useAuth } from '../auth/AuthProvider'

/**
 * Shown on a series that fell back to another language, with the reason, and for admins the way back
 * while the series still has no files.
 */
export function LanguageFallbackNotice({ seriesId }: { seriesId: number }) {
  const { t } = useLingui()
  const { can } = useAuth()
  const { data } = useSeriesLanguageFallback(seriesId)
  const revert = useRevertLanguageFallback(seriesId)
  if (!data?.active) return null

  const language = (data.language && languageName(data.language)) || data.language || ''
  return (
    <Alert
      mb="md"
      color="yellow"
      icon={<IconLanguage size={18} />}
      title={t`Fallback language: ${language}`}
    >
      <Group justify="space-between" wrap="nowrap" align="flex-start">
        <Text size="sm">
          {data.reason === 'previews' ? (
            <Trans>The sources in your main language only serve previews, so this series uses the fallback language.</Trans>
          ) : (
            <Trans>No source has this series in your main language, so it uses the fallback language.</Trans>
          )}
        </Text>
        {can('Admin') && (
          <Button
            size="xs"
            variant="default"
            loading={revert.isPending}
            onClick={() =>
              revert.mutate(undefined, {
                onSuccess: () => notifications.show({ message: now`Switched back to your main language`, color: 'var(--ok)' }),
              })
            }
          >
            <Trans>Try the main language again</Trans>
          </Button>
        )}
      </Group>
    </Alert>
  )
}
