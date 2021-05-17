﻿using CoWiN.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CoWin.Auth;
using System.Globalization;
using CoWin.Core.Exceptions;
using System.Linq;
using CoWin.Core.Validators;
using CoWin.Core.Models;

namespace CoWin.Models
{
    public class CovidVaccinationCenterFinder
    {
        private readonly IConfiguration _configuration;
        private readonly List<string> districtsToSearch = new List<string>();
        private readonly List<string> pinCodesToSearch = new List<string>();
        private string searchDate;
        private string vaccineType;                
        private readonly IValidator<string> _pinCodeValidator, _districtValidator, _mobileNumberValidator, _beneficiaryValidator;
        private readonly IValidator<SearchByDistrictModel> _searchByDistrictValidator;
        private readonly IValidator<SearchByPINCodeModel> _searchByPINCodeValidator;

        public CovidVaccinationCenterFinder()
        {
            try
            {
                _configuration = new ConfigurationBuilder()
                        .SetBasePath(Directory.GetCurrentDirectory())
                        .AddJsonFile("appsettings.json", false, true)
                        .Build();
                _pinCodeValidator = new PINCodeValidator();
                _districtValidator = new DistrictValidator();
                _mobileNumberValidator = new MobileNumberValidator();
                _beneficiaryValidator = new BeneficiaryValidator();
                _searchByDistrictValidator = new SearchByDistrictValidator(_districtValidator);
                _searchByPINCodeValidator = new SearchByPINCodeValidator(_pinCodeValidator);
            }
            catch (FormatException e)
            {
                throw new ConfigurationNotInitializedException("Oops! appsettings.json file is not in proper JSON format.", e);
            }
        }
        public void FindSlot()
        {
            CheckSoftwareVersion();
            ConfigureSearchCriteria();
            ValidateSearchCriteria();
            ValidateAuthCriteria();
            AuthenticateUser();
            SearchForAvailableSlots();
        }

        private void CheckSoftwareVersion()
        {
            var shouldAppBeAllowedToRun = new VersionChecker(_configuration).EvaluateCurrentSoftwareVersion();
            if (shouldAppBeAllowedToRun == false)
                Environment.Exit(0);
        }

        private void ValidateAuthCriteria()
        {

            if (!_mobileNumberValidator.IsValid(_configuration["CoWinAPI:Auth:Mobile"]))
            {
                throw new InvalidMobileNumberException("Invalid Mobile Number: " + _configuration["CoWinAPI:Auth:Mobile"] + " found in your config file");
            };

            foreach (var beneficiary in _configuration.GetSection("CoWinAPI:ProtectedAPI:BeneficiaryIds").Get<List<string>>())
            {
                if (!_beneficiaryValidator.IsValid(beneficiary))
                {
                    throw new InvalidBeneficiaryException("Invalid BeneficiaryId: " + beneficiary + " found in your config file.");
                }
            }
        }

        private void ValidateSearchCriteria()
        {
            DistrictValidation();

            PINCodeValidation();

            SearchByDistrictValidation();

            SearchByPINCodeValidation();

        }

        private void SearchByPINCodeValidation()
        {
            var userEnteredSearchByPINCodeDto = new SearchByPINCodeModel
            {
                IsSearchToBeDoneByPINCode = Convert.ToBoolean(_configuration["CoWinAPI:IsSearchToBeDoneByPINCode"]),
                PINCodes = pinCodesToSearch
            };

            if (!_searchByPINCodeValidator.IsValid(userEnteredSearchByPINCodeDto))
            {
                throw new InvalidMobileNumberException("Invalid Configuration for Searching by PINCode: \"IsSearchToBeDoneByPINCode\": " + userEnteredSearchByPINCodeDto.IsSearchToBeDoneByPINCode.ToString() + ", \"PINCodes\": [ " + string.Join(", ", pinCodesToSearch) + " ] found in your config file. If you want to search by PINCode, please set IsSearchToBeDoneByPINCode as true and provide proper valid values for PINCodes");
            }
        }

        private void SearchByDistrictValidation()
        {
            var userEnteredSearchByDistrictDto = new SearchByDistrictModel
            {
                IsSearchToBeDoneByDistrict = Convert.ToBoolean(_configuration["CoWinAPI:IsSearchToBeDoneByDistrict"]),
                Districts = districtsToSearch

            };

            if (!_searchByDistrictValidator.IsValid(userEnteredSearchByDistrictDto))
            {
                throw new InvalidMobileNumberException("Invalid Configuration for Searching by District: \"IsSearchToBeDoneByDistrict\": " + userEnteredSearchByDistrictDto.IsSearchToBeDoneByDistrict.ToString() + ", \"Districts\": [ " + string.Join(", ", districtsToSearch) + " ] found in your config file. If you want to search by District, please set IsSearchToBeDoneByDistrict as true and provide proper valid values for Districts");
            }
        }

        private void PINCodeValidation()
        {
            if (Convert.ToBoolean(_configuration["CoWinAPI:IsSearchToBeDoneByPINCode"]))
            {
                foreach (var pinCode in pinCodesToSearch)
                {
                    if (!_pinCodeValidator.IsValid(pinCode))
                    {
                        throw new InvalidDistrictException("Invalid PINCode: " + pinCode + " found in your config file");
                    }
                }

            }
        }

        private void DistrictValidation()
        {
            if (Convert.ToBoolean(_configuration["CoWinAPI:IsSearchToBeDoneByDistrict"]))
            {
                foreach (var district in districtsToSearch)
                {
                    if (!_districtValidator.IsValid(district))
                    {
                        throw new InvalidDistrictException("Invalid District: " + district + " found in your config file");
                    }
                }
            }
        }

        private void AuthenticateUser()
        {
            new OTPAuthenticator(_configuration).ValidateUser();
        }

        private void SearchForAvailableSlots()
        {
            for (int i = 1; i < Convert.ToInt32(_configuration["CoWinAPI:TotalIterations"]); i++)
            {
                if (CovidVaccinationCenter.IS_BOOKING_SUCCESSFUL == true)
                {
                    return;
                }

                Console.ResetColor();
                Console.WriteLine($"Fetching Resources, Try #{i}");

                /* Seaching with be either by PIN or District or Both; By Default by PIN.
                 * If Both are selected for searching, PIN will be given Preference Over District
                 */
                if (Convert.ToBoolean(_configuration["CoWinAPI:IsSearchToBeDoneByPINCode"]))
                {
                    foreach (var pinCode in pinCodesToSearch)
                    {
                        new CovidVaccinationCenter(_configuration).GetSlotsByPINCode(pinCode, searchDate, vaccineType);

                        if (CovidVaccinationCenter.IS_BOOKING_SUCCESSFUL == true)
                        {
                            return;
                        }

                    }
                }
                if (Convert.ToBoolean(_configuration["CoWinAPI:IsSearchToBeDoneByDistrict"]))
                {
                    foreach (var district in districtsToSearch)
                    {
                        new CovidVaccinationCenter(_configuration).GetSlotsByDistrictId(district, searchDate, vaccineType);

                        if (CovidVaccinationCenter.IS_BOOKING_SUCCESSFUL == true)
                        {
                            return;
                        }

                    }
                }

                Thread.Sleep(Convert.ToInt32(_configuration["CoWinAPI:SleepIntervalInMilliseconds"]));
            }
        }

        private void ConfigureSearchCriteria()
        {
            /* Seaching with be either by PIN or District or Both; By Default by PIN.
            * If Both are selected for searching, PIN will be given Preference Over District
            */
            foreach (var item in _configuration.GetSection("CoWinAPI:Districts").Get<List<string>>())
            {
                districtsToSearch.Add(item);
            }

            foreach (var item in _configuration.GetSection("CoWinAPI:PINCodes").Get<List<string>>())
            {
                pinCodesToSearch.Add(item);
            }

            if (!string.IsNullOrEmpty(_configuration["CoWinAPI:DateToSearch"]))
            {
                searchDate = DateTime.ParseExact(_configuration["CoWinAPI:DateToSearch"], "dd-MM-yyyy", new CultureInfo("en-US")).ToString("dd-MM-yyyy");
            }
            else
            {
                // BY DEFAULT, When CoWinAPI:DateToSearch is Blank, Next Day is chosen as the default date to search for Vaccine
                searchDate = DateTime.Now.AddDays(1).ToString("dd-MM-yyyy");
            }
            vaccineType = _configuration["CoWinAPI:VaccineType"];
        }

    }
}
