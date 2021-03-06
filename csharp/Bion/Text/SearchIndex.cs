// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;

using Bion.IO;

namespace Bion
{
    /// <summary>
    ///  SearchIndexWriter writes a word index, which lists the positions where each
    ///  occurrence of a set of words was found.
    /// </summary>
    /// <remarks>
    ///  The binary file format is:
    ///    - For each word, write the delta of this occurrence after the last one as a 7-bit encoded terminated integer.
    ///    - Next, write a four-byte integer with the absolute offset where the matches for each word begin.
    ///    - Last, write a four-byte integer with the total word count in the index.
    /// </remarks>
    public class SearchIndexWriter : IDisposable
    {
        internal const int Shift = 2;

        private string OutputPath;
        private string WorkingPath;
        private int BlockCount;
        private byte[] WriterBuffer;

        private int WordCount;
        private int[] FirstWordMatch;
        private int[] LastWordMatch;
        private long[] MatchPositions;
        private int[] NextMatchIndex;
        private int Count;

        public long WordTotal { get; private set; }
        public long NonDuplicateTotal { get; private set; }

        public SearchIndexWriter(string outputPath, int wordCount, int size)
        {
            OutputPath = outputPath;
            WorkingPath = Path.ChangeExtension(outputPath, ".Working");
            
            WriterBuffer = new byte[4096];

            WordCount = wordCount;
            FirstWordMatch = new int[wordCount];
            LastWordMatch = new int[wordCount];
            MatchPositions = new long[size];
            NextMatchIndex = new int[size];

            Directory.CreateDirectory(WorkingPath);

            Reset();
        }

        private void Reset()
        {
            Count = 0;

            Array.Fill(FirstWordMatch, -1);
            Array.Fill(LastWordMatch, -1);
        }

        /// <summary>
        ///  Add an entry for the given word at the given file position.
        /// </summary>
        /// <param name="wordIndex">Index of Word (from Word Compressor list)</param>
        /// <param name="position">Byte offset where word appears</param>
        public void Add(int wordIndex, long position)
        {
            WordTotal++;
            int matchIndex = Count;

            int last = LastWordMatch[wordIndex];
            if (last != -1)
            {
                if (MatchPositions[last] == position)
                {
                    return;
                }
                else
                {
                    NextMatchIndex[last] = matchIndex;
                }
            }
            else
            {
                FirstWordMatch[wordIndex] = matchIndex;
            }

            MatchPositions[matchIndex] = position;
            NextMatchIndex[matchIndex] = -1;
            LastWordMatch[wordIndex] = matchIndex;

            Count++;
            NonDuplicateTotal++;

            if (Count == MatchPositions.Length)
            {
                Flush();
            }
        }

        private void Flush()
        {
            if (Count == 0) { return; }

            string filePath = Path.Combine(WorkingPath, $"{BlockCount}.idx");
            using (SearchIndexSliceWriter writer = new SearchIndexSliceWriter(new BufferedWriter(File.Create(filePath), WriterBuffer), WordCount))
            {
                for (int wordIndex = 0; wordIndex < WordCount; ++wordIndex)
                {
                    int matchIndex = FirstWordMatch[wordIndex];
                    while (matchIndex != -1)
                    {
                        writer.WritePosition(MatchPositions[matchIndex]);
                        matchIndex = NextMatchIndex[matchIndex];
                    }

                    writer.NextWord();
                }
            }

            BlockCount++;
            Reset();
        }

        private void Merge()
        {
            if (BlockCount == 1)
            {
                if (File.Exists(OutputPath)) { File.Delete(OutputPath); }
                File.Move(Path.Combine(WorkingPath, "0.idx"), OutputPath);
                Directory.Delete(WorkingPath, true);
                return;
            }

            SearchIndexSliceWriter writer = new SearchIndexSliceWriter(new BufferedWriter(File.Create(OutputPath), WriterBuffer), WordCount);
            SearchIndexReader[] readers = new SearchIndexReader[BlockCount];
            try
            {
                for (int i = 0; i < readers.Length; ++i)
                {
                    readers[i] = new SearchIndexReader(Path.Combine(WorkingPath, $"{i}.idx"));
                }

                long[] positions = new long[256];

                for (int wordIndex = 0; wordIndex < WordCount; ++wordIndex)
                {
                    for (int readerIndex = 0; readerIndex < readers.Length; ++readerIndex)
                    {
                        SearchResult result = readers[readerIndex].Find(wordIndex);
                        while(!result.Done)
                        {
                            int count = result.Page(ref positions);
                            for (int i = 0; i < count; ++i)
                            {
                                writer.WritePosition(positions[i]);
                            }
                        }
                    }

                    writer.NextWord();
                }
            }
            finally
            {
                for (int i = 0; i < readers.Length; ++i)
                {
                    if (readers[i] != null)
                    {
                        readers[i].Dispose();
                        readers[i] = null;
                    }
                }

                if (writer != null)
                {
                    writer.Dispose();
                    writer = null;
                }

                Directory.Delete(WorkingPath, true);
            }
        }

        public void Dispose()
        {
            Flush();
            Merge();
        }
    }

    internal class SearchIndexSliceWriter : IDisposable
    {
        private BufferedWriter _writer;
        private int _wordCount;
        private int[] _firstPositionPerWord;

        private long _lastPosition;
        private int _currentWordIndex;

        public SearchIndexSliceWriter(BufferedWriter writer, int wordCount)
        {
            _writer = writer;
            _wordCount = wordCount;
            _firstPositionPerWord = new int[wordCount];

            _currentWordIndex = -1;
            NextWord();
        }

        /// <summary>
        ///  Write the next position for the current word.
        /// </summary>
        /// <param name="position">Position in file where word occurs</param>
        public void WritePosition(long position)
        {
            // Shift to reduce size; only an approximate position will be returned
            position = position >> SearchIndexWriter.Shift;

            if (_lastPosition == -1)
            {
                NumberConverter.WriteSevenBitTerminated(_writer, (ulong)position);
            }
            else if (position < _lastPosition)
            {
                throw new ArgumentException($"WritePosition must be given positions in ascending order. Position {position:n0} was less than previous position {_lastPosition:n0}.");
            }
            else if (position != _lastPosition)
            {
                NumberConverter.WriteSevenBitTerminated(_writer, (ulong)(position - _lastPosition));
            }

            _lastPosition = position;
        }

        /// <summary>
        ///  Indicate we're writing matches for the next word now.
        /// </summary>
        public void NextWord()
        {
            _lastPosition = -1;
            _currentWordIndex++;
            if (_currentWordIndex < _wordCount)
            {
                _firstPositionPerWord[_currentWordIndex] = (int)_writer.BytesWritten;
            }
        }

        private void WriteIndexMap()
        {
            if (_currentWordIndex < _wordCount) { throw new InvalidOperationException($"SearchIndexSliceWriter Dispose called before all {_wordCount:n0} words had positions written. Only {_currentWordIndex:n0} words have been written."); }

            // Write the position where the matches for each word begin
            NumberConverter.WriteIntBlock(_writer, _firstPositionPerWord, _wordCount);

            // Write the word count
            _writer.Write(_wordCount);
        }

        public void Dispose()
        {
            if (_writer != null)
            {
                // Write the map at the end of the index
                WriteIndexMap();

                _writer.Dispose();
                _writer = null;
            }
        }
    }

    /// <summary>
    ///  SearchIndexReader reads a word index, containing the positions of matches
    ///  for each word.
    /// </summary>
    public class SearchIndexReader : IDisposable
    {
        private BufferedReader _reader;
        private int[] _firstMatchOffset;
        public int WordCount => _firstMatchOffset.Length - 1;

        /// <summary>
        ///  Build a reader for the given index.
        ///  This will load a small amount of data identifying where the matches
        ///  for each word begin in the index.
        /// </summary>
        /// <param name="indexPath">File Path of index to load</param>
        public SearchIndexReader(string indexPath)
        {
            _reader = new BufferedReader(File.OpenRead(indexPath));

            // Read word count (last four bytes)
            _reader.Seek(-4, SeekOrigin.End);
            int wordCount = _reader.ReadInt32();

            // Read start offset for each word's matches (just before count)
            _firstMatchOffset = new int[wordCount + 1];
            _reader.Seek(-4 * (wordCount + 1), SeekOrigin.End);
            _firstMatchOffset[wordCount] = (int)_reader.BytesRead;
            NumberConverter.ReadIntBlock(_reader, _firstMatchOffset, wordCount);
        }

        /// <summary>
        ///  Return a SearchResult with all results for the given word index.
        /// </summary>
        /// <param name="wordIndex">Index of word to find matches for</param>
        /// <returns>SearchResult to read all matches</returns>
        public SearchResult Find(int wordIndex)
        {
            long startOffset = _firstMatchOffset[wordIndex];
            long endOffset = _firstMatchOffset[wordIndex + 1];
            return new SearchResult(_reader, startOffset, endOffset);
        }

        public void Dispose()
        {
            if (_reader != null)
            {
                _reader.Dispose();
                _reader = null;
            }
        }
    }

    public interface ISearchResult
    {
        bool Done { get; }
        int Page(ref long[] matches);
    }

    public class EmptyResult : ISearchResult
    {
        public static EmptyResult Instance = new EmptyResult();

        public bool Done => true;

        public int Page(ref long[] matches)
        {
            return 0;
        }
    }

    public class SearchResult : ISearchResult
    {
        private BufferedReader Reader;

        private long IndexStartOffset;
        private long IndexEndOffset;

        private long CurrentOffset;
        private long LastValue;

        public SearchResult(BufferedReader reader, long startOffset, long endOffset)
        {
            Reader = reader;
            IndexStartOffset = startOffset;
            IndexEndOffset = endOffset;
            CurrentOffset = IndexStartOffset;
        }

        public bool Done => CurrentOffset >= IndexEndOffset;

        public int Page(ref long[] matches)
        {
            int lengthLeft = (int)(IndexEndOffset - CurrentOffset);
            int lengthToGet = Math.Min(lengthLeft, matches.Length * 10);

            // Read the match bytes
            Reader.Seek(CurrentOffset, SeekOrigin.Begin);
            Reader.EnsureSpace(lengthToGet, lengthLeft);

            // Decode to non-relative longs
            int count = 0;
            while (Reader.BytesRead < IndexEndOffset && count < matches.Length)
            {
                long value = LastValue + (long)NumberConverter.ReadSevenBitTerminated(Reader);
                matches[count++] = value << SearchIndexWriter.Shift;
                LastValue = value;
            }

            CurrentOffset = Reader.BytesRead;
            return count;
        }
    }
}
