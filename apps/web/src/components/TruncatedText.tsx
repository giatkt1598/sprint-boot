import Tooltip from '@mui/material/Tooltip';
import { useState, type CSSProperties } from 'react';

/** One line cut with an ellipsis; hovering it shows the whole text, but only when something was cut. */
export function TruncatedText({ text, style }: { text: string; style?: CSSProperties }) {
  const [cut, setCut] = useState(false);

  return (
    <Tooltip title={cut ? text : ''} placement="top-start" disableInteractive>
      <span
        className="min-w-0 truncate"
        style={style}
        onMouseEnter={(event) => setCut(event.currentTarget.scrollWidth > event.currentTarget.clientWidth)}
      >
        {text}
      </span>
    </Tooltip>
  );
}
