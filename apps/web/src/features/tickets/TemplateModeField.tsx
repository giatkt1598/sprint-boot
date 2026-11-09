import FormControlLabel from '@mui/material/FormControlLabel';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import Typography from '@mui/material/Typography';
import { useId } from 'react';
import { useTranslation } from 'react-i18next';
import { font, sem } from '../../theme/tokens';
import { TemplateMode, type TemplateModeValue } from './api';

/** Fill puts the template into the description; placeholder only shows it until someone types. */
export function TemplateModeField({ value, onChange }: { value: TemplateModeValue; onChange: (mode: TemplateModeValue) => void }) {
  const { t } = useTranslation();
  const labelId = useId();
  const option = (mode: TemplateModeValue, key: 'fill' | 'placeholder') => (
    <FormControlLabel
      value={String(mode)}
      control={<Radio size="small" />}
      sx={{ alignItems: 'flex-start', marginInline: 0, gap: 1 }}
      label={
        <span className="flex flex-col pt-2">
          <span>{t(`templates.mode.${key}`)}</span>
          <Typography variant="body2" color="textSecondary" component="span">{t(`templates.mode.${key}Hint`)}</Typography>
        </span>
      }
    />
  );

  return (
    <div className="flex flex-col gap-1">
      <span id={labelId} style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>
        {t('templates.mode.label')}
      </span>
      <RadioGroup aria-labelledby={labelId} value={String(value)} onChange={(event) => onChange(Number(event.target.value) as TemplateModeValue)}>
        {option(TemplateMode.fill, 'fill')}
        {option(TemplateMode.placeholder, 'placeholder')}
      </RadioGroup>
    </div>
  );
}
